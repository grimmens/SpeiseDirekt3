using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Client;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;
using System.Collections.Concurrent;
using System.Data.SqlTypes;
using System.Text.Json;

namespace SpeiseDirekt3.ServiceImplementation
{
    // Helper DTO to capture the AI-generated data.
    public class MenuItemData
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Allergens { get; set; } = string.Empty;
    }

    public class AiMenuItemGenerator : IMenuItemGenerator
    {
        private readonly IChatClient _chatClient;
        private readonly IServiceProvider serviceProvider;

        // Inject the IChatClient instance (configured in DI).
        public AiMenuItemGenerator(IChatClient chatClient, IServiceProvider serviceProvider)
        {
            _chatClient = chatClient;
            this.serviceProvider = serviceProvider;
        }

        class JsonResult
        {
            public string Name { get; set; }
        }
        public async Task CreateMenus(Action onCreated, int numberOfEntries = 100)
        {
            int z = 0;
            while (z++ < numberOfEntries)
            {
                using var scope = serviceProvider.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                using var tr = context.Database.BeginTransaction();
                Console.WriteLine($"create {z}. menu");
                string menuName = "Dinner" + Guid.NewGuid().ToString();
                var menu = new Menu()
                {
                    Description = "A comprehensive dinner menu",
                    Name = menuName,
                    Theme = DesignTheme.Elegant,
                };
                context.Menus.Add(menu);
                context.SaveChanges();
                for (int i = 0; i < 5; i++)
                {
                    string prompt = "einen fiktiven Kategorie eintrag für ein Restaurantmenü. " +
                        "Gib eine kurze Bezeichnung einer Kategorie eines Restaurant-Menüs aus ein bis drei Wörter zurück" +
                        "Gib ein JSON-Objekt mit dem Schlüssel 'Name' zurück.";
                    
                    string? result = null;

                    ChatResponse<JsonResult> aiResponse;
                    try
                    {
                        aiResponse = await _chatClient.GetResponseAsync<JsonResult>(prompt);
                        result = aiResponse.Result.Name;
                    } catch (Exception ex) {
                        Console.WriteLine($"AI request error: {ex.Message}");
                        i--;
                        continue;
                    }
                    
                    var category = new Category { Name = result, MenuId = menu.Id };
                    context.Add(category);
                    context.SaveChanges();
                    var menuEntries = await CreateMockMenuEntriesAsync(category, 4);
                    context.AddRange(menuEntries.ToList());
                    context.SaveChanges();
                }
                ChatResponse<JsonResult> qrResponse;
                bool retryQrMenuCreation = true;
                string? qrMenuTitle = default;
                while(retryQrMenuCreation)
                try
                {
                    qrResponse = await _chatClient.GetResponseAsync<JsonResult>("Erzeuge einen Titel für ein QR-Code Menü nicht länger als 10 Wörter." + "Gib ein JSON-Objekt mit dem Schlüssel 'Name' zurück.");
                    qrMenuTitle = qrResponse.Result.Name;
                    retryQrMenuCreation = false;
                } catch (Exception ex) 
                {
                    Console.WriteLine($"AI request error: {ex.Message}");
                }
                
                var qrcode = new QRCode()
                {
                    CreatedAt = DateTime.UtcNow,
                    MenuId = menu.Id,
                    Title = qrMenuTitle
                };
                context.Add(qrcode);
                context.SaveChanges();
                tr.Commit();
                onCreated.Invoke();
            }
        }

        public async Task<IEnumerable<MenuItem>> CreateMockMenuEntriesAsync(Category category, int numberOfEntries)
        {
            var newMenuItems = new ConcurrentBag<MenuItem>();

            await Parallel.ForAsync(0, numberOfEntries, async (i, ct) =>
            {
                var entry = await CreateMockMenuEntry(category);
                if (entry != null)
                    newMenuItems.Add(entry);
            });

            //for(int i = 0; i < numberOfEntries; i++)
            //{
            //    var entry = await CreateMockMenuEntry(category);
            //    if (entry != null)
            //        newMenuItems.Add(entry);
            //};

            return newMenuItems;
        }


        private async Task<MenuItem?> CreateMockMenuEntry(Category category)
        {
            string prompt = $"Erstelle einen fiktiven Menüeintrag (deutsch) für ein Restaurant. " +
                $"Gib ein JSON-Objekt mit den Schlüsseln 'Name', 'Description' und 'Allergens' zurück, das ein einzigartiges Gericht beschreibt. " +
                $"Stelle sicher, dass das Gericht zur Kategorie {category.Name} passt. Allergene sind als eine Liste von Großbuchstaben zu beschreiben z.B. \"F, S, M\"  (F = Fisch, S = Schalenfrüchte, M = Milchprodukte).";


            ChatResponse<MenuItemData> aiResponse;
            try
            {
                aiResponse = await _chatClient.GetResponseAsync<MenuItemData>(prompt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AI request error: {ex.Message}");
                return null;
            }

            // Attempt to deserialize the AI response.
            MenuItemData? data = null;
            try
            {
                data = aiResponse.Result;
                //data = JsonSerializer.Deserialize<MenuItemData>(aiResponse.Text);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing AI response: {ex.Message}");
                return null;

            }


            // Create the MenuItem entity.
            var menuItem = new MenuItem
            {
                Id = Guid.NewGuid(),
                Name = data.Name,
                Description = data.Description,
                Allergens = data.Allergens,
                Price = (decimal)(Random.Shared.NextDouble() * 20.0), // Example pricing logic.
                CategoryId = category.Id
            };
            return menuItem;
        }
    }


}
