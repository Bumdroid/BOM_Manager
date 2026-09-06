namespace BOMManager.Models
{
    public class AssemblyInfo
    {
        public string Title { get; set; } = "No Assembly";
        public string Path { get; set; } = string.Empty;
        public string ActiveConfiguration { get; set; } = string.Empty;
        public int TotalComponentsCount { get; set; } = 0;
        public int UniquePartsCount { get; set; } = 0;
        public bool IsConnected { get; set; } = false;
        public string? ErrorMessage { get; set; }
    }
}
