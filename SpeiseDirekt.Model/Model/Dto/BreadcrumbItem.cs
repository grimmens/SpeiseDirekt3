namespace SpeiseDirekt.Model
{
    public class BreadcrumbItem
    {
        public string Title { get; set; }
        public string Link { get; set; }

        public BreadcrumbItem(string title, string link)
        {
            Title = title;
            Link = link;
        }
    }
}
