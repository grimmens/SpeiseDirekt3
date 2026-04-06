using System.ComponentModel.DataAnnotations.Schema;

namespace SpeiseDirekt.Model
{
    public class MenuComboItem : IAppUserEntity
    {
        public Guid Id { get; set; }

        [ForeignKey(nameof(MenuCombo))]
        public Guid MenuComboId { get; set; }
        public MenuCombo? MenuCombo { get; set; }

        [ForeignKey(nameof(MenuItem))]
        public Guid MenuItemId { get; set; }
        public MenuItem? MenuItem { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
