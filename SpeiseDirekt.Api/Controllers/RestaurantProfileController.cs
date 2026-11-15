using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

/// <summary>The current tenant's restaurant profile (one per tenant, created by the first <c>PUT</c>).</summary>
[Route("api/restaurant-profile")]
[ApiController]
[Authorize]
public class RestaurantProfileController : ControllerBase
{
    private readonly IRestaurantProfileService _profiles;
    private readonly ApplicationDbContext _db;

    public RestaurantProfileController(IRestaurantProfileService profiles, ApplicationDbContext db)
    {
        _profiles = profiles;
        _db = db;
    }

    /// <summary>The profile of the current tenant; 404 when none has been saved yet.</summary>
    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewMenus)]
    public async Task<ActionResult<RestaurantProfile>> Get()
    {
        var profile = await _profiles.GetOwnAsync();
        return profile is null ? NotFound() : Ok(profile);
    }

    /// <summary>Creates (201) or updates (200) the profile of the current tenant.</summary>
    [HttpPut]
    [Authorize(Policy = PolicyNames.CanEditMenus)]
    public async Task<ActionResult<RestaurantProfile>> Put(RestaurantProfileDto dto)
    {
        var current = await _profiles.GetOwnAsync();

        var logo = await ResolveImagePathAsync(dto.LogoImagePath, current?.LogoImagePath, nameof(dto.LogoImagePath));
        var cover = await ResolveImagePathAsync(dto.CoverImagePath, current?.CoverImagePath, nameof(dto.CoverImagePath));
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        RestaurantProfileSaveResult result;
        try
        {
            result = await _profiles.SaveOwnAsync(new RestaurantProfile
            {
                Name = dto.Name,
                Description = dto.Description,
                LogoImagePath = logo,
                CoverImagePath = cover,
                Phone = dto.Phone,
                Email = dto.Email,
                Website = dto.Website,
                InstagramUrl = dto.InstagramUrl,
                FacebookUrl = dto.FacebookUrl,
                GoogleReviewUrl = dto.GoogleReviewUrl,
                Address = dto.Address,
                OpeningHours = dto.OpeningHours
            });
        }
        catch (ValidationException ex)
        {
            // The DTO is validated first; this catches what only the normalized values reveal (e.g. a name of spaces)
            foreach (var member in ex.ValidationResult.MemberNames.DefaultIfEmpty(string.Empty))
                ModelState.AddModelError(member, ex.ValidationResult.ErrorMessage ?? "Invalid value.");
            return ValidationProblem(ModelState);
        }

        return result.Created
            ? CreatedAtAction(nameof(Get), null, result.Profile)
            : Ok(result.Profile);
    }

    /// <summary>
    /// <c>null</c> keeps <paramref name="currentPath"/>, an empty string removes the image, anything else must be the
    /// <c>image/{id}</c> path of an existing image (otherwise a model error for <paramref name="field"/>).
    /// </summary>
    private async Task<string?> ResolveImagePathAsync(string? requested, string? currentPath, string field)
    {
        if (requested is null)
            return currentPath;
        if (string.IsNullOrWhiteSpace(requested))
            return null;

        var path = requested.Trim();
        if (!ImagePaths.TryGetImageId(path, out var imageId) || !await _db.Images.AnyAsync(i => i.Id == imageId))
        {
            ModelState.AddModelError(field, "Use the image/{id} path of an image uploaded with POST /api/images.");
            return currentPath;
        }
        return ImagePaths.ForImage(imageId);
    }
}
