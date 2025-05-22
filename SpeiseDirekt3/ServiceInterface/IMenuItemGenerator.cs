using Microsoft.AspNetCore.Components;
using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.ServiceInterface
{
    public interface IMenuItemGenerator
    {
        /// <summary>
        /// Generates a collection of mock menu entries for a given category using AI.
        /// </summary>
        /// <param name="categoryId">The category to which the new menu entries belong.</param>
        /// <param name="numberOfEntries">How many entries to create.</param>
        /// <returns>A collection of newly created MenuItem objects.</returns>
        Task<IEnumerable<MenuItem>> CreateMockMenuEntriesAsync(Category category, int numberOfEntries);

        Task CreateMenus(Action onCreated, int number = 100);
    }
}
