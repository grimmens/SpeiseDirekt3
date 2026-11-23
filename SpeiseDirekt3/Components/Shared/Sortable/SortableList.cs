using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace SpeiseDirekt3.Components.Shared.Sortable;

/// <summary>A row moved from <see cref="OldIndex"/> to <see cref="NewIndex"/> (positions in the list as shown).</summary>
public sealed record SortableMove(int OldIndex, int NewIndex);

/// <summary>What the row template of <see cref="SortableList{TItem}"/> gets: the item, its position and the grip handle.</summary>
public sealed record SortableRow<TItem>(TItem Item, int Index, int Count, RenderFragment Handle);

/// <summary>
/// A list whose rows can be reordered by dragging their grip handle (mouse, touch, pen) or with ↑/↓ on the focused
/// handle (js/sortable-list.js, loaded only when a sortable list is on the page). Blazor keeps owning the DOM: the
/// script only translates rows while dragging and reports <c>(oldIndex, newIndex)</c> on drop; <see cref="OnMove"/>
/// reorders the parent's list (optimistically: re-render first, then save) and returns whether the new order was
/// kept — false = rolled back. The result is spoken through the shared live region (<c>#sd-announcer</c>,
/// Layout/MainLayout): "Moved Desserts to position 3 of 4." After a keyboard move the focus returns to the moved
/// row's handle.
/// <para>
/// The component renders the container (<see cref="Element"/>, e.g. <c>tbody</c> or <c>ul</c>) and one keyed row
/// element per item (<see cref="RowElement"/>, e.g. <c>tr</c> or <c>li</c>); the template renders the row's content
/// and places <see cref="SortableRow{TItem}.Handle"/> where the grip goes. Without <see cref="OnMove"/> (no edit
/// permission) the handle renders nothing and no script is loaded.
/// </para>
/// </summary>
public sealed class SortableList<TItem> : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The handle's label and the announcements (Sort_* in Resources/ShellText.resx).</summary>
    [Inject] private IStringLocalizer<ShellText> T { get; set; } = default!;

    [Parameter, EditorRequired] public IReadOnlyList<TItem> Items { get; set; } = Array.Empty<TItem>();

    /// <summary>Stable identity of a row (Blazor's <c>@key</c> and <c>data-sortable-key</c>).</summary>
    [Parameter, EditorRequired] public Func<TItem, object> KeyOf { get; set; } = default!;

    /// <summary>The row's name in the handle's label ("Reorder Desserts") and in the announcements.</summary>
    [Parameter, EditorRequired] public Func<TItem, string> NameOf { get; set; } = default!;

    [Parameter, EditorRequired] public RenderFragment<SortableRow<TItem>> ChildContent { get; set; } = default!;

    /// <summary>Applies a move; true = kept, false = rolled back. Null: the list cannot be reordered.</summary>
    [Parameter] public Func<SortableMove, Task<bool>>? OnMove { get; set; }

    [Parameter] public string Element { get; set; } = "ul";
    [Parameter] public string RowElement { get; set; } = "li";
    [Parameter] public string? RowClass { get; set; }

    /// <summary>Additional classes per row (e.g. a highlight), appended to <see cref="RowClass"/>.</summary>
    [Parameter] public Func<TItem, string?>? RowClassOf { get; set; }

    /// <summary>Extra attributes per row (test ids, data attributes).</summary>
    [Parameter] public Func<TItem, IReadOnlyDictionary<string, object>>? RowAttributes { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private ElementReference container;
    private IJSObjectReference? module;
    private IJSObjectReference? handle;
    private DotNetObjectReference<SortableList<TItem>>? selfRef;
    private bool attaching;
    private bool busy;
    private string? focusKey;
    private string? announcement;
    private bool disposed;

    private bool Sortable => OnMove is not null;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, Element);
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        if (Sortable)
            builder.AddAttribute(2, "data-sortable", "");
        builder.AddElementReferenceCapture(3, r => container = r);

        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            var key = KeyOf(item);
            builder.OpenElement(10, RowElement);
            builder.SetKey(key);
            if (RowAttributes is not null)
                builder.AddMultipleAttributes(11, RowAttributes(item));
            var extra = RowClassOf?.Invoke(item);
            builder.AddAttribute(12, "class", string.IsNullOrEmpty(extra) ? RowClass : $"{RowClass} {extra}");
            builder.AddAttribute(13, "data-sortable-item", "");
            builder.AddAttribute(14, "data-sortable-key", KeyText(key));
            builder.AddContent(15, ChildContent(new SortableRow<TItem>(item, i, Items.Count, HandleFor(item))));
            builder.CloseElement();
        }

        builder.CloseElement();
    }

    private static string KeyText(object key) => key.ToString() ?? "";

    /// <summary>The six-dot grip: a button so it is focusable and keyboard-operable (↑/↓).</summary>
    private RenderFragment HandleFor(TItem item) => builder =>
    {
        if (!Sortable)
            return;
        var name = NameOf(item);
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "class", "sd-sort-handle");
        builder.AddAttribute(3, "data-sortable-handle", "");
        builder.AddAttribute(4, "aria-label", T["Sort_Handle", name].Value);
        builder.AddAttribute(5, "aria-describedby", "sd-sort-help");
        builder.AddAttribute(6, "title", T["Sort_HandleTitle"].Value);
        builder.AddAttribute(7, "data-testid", "sort-handle");
        builder.AddMarkupContent(8,
            "<svg viewBox=\"0 0 20 20\" fill=\"currentColor\" aria-hidden=\"true\">" +
            "<circle cx=\"7\" cy=\"4.5\" r=\"1.5\"/><circle cx=\"13\" cy=\"4.5\" r=\"1.5\"/>" +
            "<circle cx=\"7\" cy=\"10\" r=\"1.5\"/><circle cx=\"13\" cy=\"10\" r=\"1.5\"/>" +
            "<circle cx=\"7\" cy=\"15.5\" r=\"1.5\"/><circle cx=\"13\" cy=\"15.5\" r=\"1.5\"/></svg>");
        builder.CloseElement();
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (disposed || !RendererInfo.IsInteractive)
            return;
        try
        {
            // Once: a render while the import is still loading runs this again before `handle` is set, and a second
            // attach would double every listener (one arrow key moved the row twice, i.e. back to where it was)
            if (Sortable && handle is null && !attaching)
            {
                attaching = true;
                try
                {
                    selfRef ??= DotNetObjectReference.Create(this);
                    module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./js/sortable-list.js");
                    handle = await module.InvokeAsync<IJSObjectReference>("attach", container, selfRef);
                }
                finally
                {
                    attaching = false;
                }
            }
            if (module is null)
                return;
            if (focusKey is { } key)
            {
                focusKey = null;
                await module.InvokeVoidAsync("focusHandle", container, key);
            }
            if (announcement is { } text)
            {
                announcement = null;
                await module.InvokeVoidAsync("announce", text);
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
            // Script not loaded: the list simply cannot be dragged
        }
    }

    /// <summary>A pointer drag ended on another slot.</summary>
    [JSInvokable]
    public Task<bool> OnDropJs(int oldIndex, int newIndex) => MoveAsync(oldIndex, newIndex, keyboard: false);

    /// <summary>↑/↓ on a focused handle (the new index may lie outside the list: then nothing moves).</summary>
    [JSInvokable]
    public Task<bool> OnKeyMoveJs(int oldIndex, int newIndex) => MoveAsync(oldIndex, newIndex, keyboard: true);

    private async Task<bool> MoveAsync(int oldIndex, int newIndex, bool keyboard)
    {
        if (busy || OnMove is null || oldIndex < 0 || oldIndex >= Items.Count)
            return false;

        var item = Items[oldIndex];
        var name = NameOf(item);
        var count = Items.Count;
        if (keyboard)
            focusKey = KeyText(KeyOf(item));

        if (newIndex == oldIndex)
            return false;
        if (newIndex < 0 || newIndex >= count)
        {
            announcement = newIndex < 0 ? T["Sort_AlreadyFirst", name] : T["Sort_AlreadyLast", name];
            StateHasChanged();
            return false;
        }

        busy = true;
        bool kept;
        try
        {
            kept = await OnMove(new SortableMove(oldIndex, newIndex));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reordering: {ex}");
            kept = false;
        }
        finally
        {
            busy = false;
        }

        announcement = kept
            ? T["Sort_Moved", name, newIndex + 1, count]
            : T["Sort_Failed", name];
        if (!disposed)
            StateHasChanged();
        return kept;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        try
        {
            if (handle is not null)
            {
                await handle.InvokeVoidAsync("dispose");
                await handle.DisposeAsync();
            }
            if (module is not null)
                await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (JSException)
        {
        }
        selfRef?.Dispose();
    }
}
