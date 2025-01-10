using Fz.Platform.Office.Excel;

namespace PdfReader
{
	public class WordExcel
	{
		[ExcelColumn("书籍名称")]
		public string? BookName { get; set; }
		[ExcelColumn("目录名称")]
		public string? CatalogueName { get; set; }
		[ExcelColumn("单词")]
		public string? WordText { get; set; }
		[ExcelColumn("释义")]
        public string? TranslateText { get; set; }
    }
}
