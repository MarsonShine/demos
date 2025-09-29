using Fz.Platform.Office.Excel;

namespace UserCase
{
    public class InputData
    {
        [ExcelColumn("Id")]
        public int Id { get; set; }
        [ExcelColumn("CatalogueId")]
        public int CatalogueId { get; set; }
        [ExcelColumn("Content")]
        public string? Content { get; set; }
        [ExcelColumn("AudioUrl")]
        public string? AudioUrl { get; set; }
    }
}
