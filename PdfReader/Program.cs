// See https://aka.ms/new-console-template for more information
using Fz.Platform.Office;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Layout;
using iText.Layout.Properties;
using PdfReader;

Console.WriteLine("Hello, World!");

using var document = new Document(new PdfDocument(new iText.Kernel.Pdf.PdfReader("xxxx.pdf")));
using var pdfDocument = document.GetPdfDocument();
var pageNumber = pdfDocument.GetNumberOfPages();

Console.WriteLine($"Number of pages: {pageNumber}");
await ExtractWordTextFromPdfPage();


//ExtractSentenceTextFromPdfPage();

void ExtractSentenceTextFromPdfPage()
{
	var contentPages = Enumerable.Range(7, 20);
	var pdfPage = pdfDocument.GetPage(7);
	var strategy = new SimpleTextExtractionStrategy();
	string pageText = PdfTextExtractor.GetTextFromPage(pdfPage, strategy);
	Console.WriteLine($"Content of pages {91}: {pageText}");
}

async Task ExtractWordTextFromPdfPage()
{
	List<WordExcel> list = [];
	int[] pageNumbers = [71, 72, 73];
	for (int i = 0; i < pageNumbers.Length; i++)
	{
		int number = pageNumbers[i];
		var pdfPage = pdfDocument.GetPage(number);
		var strategy = new SimpleTextExtractionStrategy();
		string pageText = PdfTextExtractor.GetTextFromPage(pdfPage, strategy);
		Console.WriteLine($"Content of pages {number}: {pageText}");

		string[] lines = pageText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		string catalogueName = "";
		foreach (string line in lines)
		{
			var catalogue = GetCagalogueName(line.Trim(), catalogueName);
			if (catalogue != line)
			{
				// 首先按全角空格 '　' 分割
				var parts = line.Split([" "], StringSplitOptions.RemoveEmptyEntries);

				if (parts.Length >= 2)
				{
					string word = parts[0].Trim();
					string meaning = string.Join("", parts[1..]);

					list.Add(new WordExcel
					{
						WordText = word,
						TranslateText = meaning,
						CatalogueName = catalogue,
						BookName = "xxxx"
					});
				}
				else
				{
					list.Add(new WordExcel
					{
						WordText = parts[0].Trim(),
						TranslateText = "",
						CatalogueName = catalogue,
						BookName = "xxx"
					});
				}
			}
			catalogueName = catalogue;
		}
	}
	// 导出excel
	var excelBuffer = new ExcelHelper().Export(list, "xxx.xlsx");
	await File.WriteAllBytesAsync("xxx.xlsx", excelBuffer);
}

static string GetCagalogueName(string line, string catalogueName) => line switch
{
	"Welcome" or
	"Unit 1" or "Module 1" or
	"Unit 2" or "Module 2" or
	"Unit 3" or "Module 3" or
	"Unit 4" or "Module 4" or
	"Unit 5" or "Module 5" or
	"Unit 6" or "Module 6" or
	"Unit 7" or "Module 7" or
	"Unit 8" or "Module 8" or
	"Unit 9" or "Module 9" or
	"Unit 10" or "Module 10"
	=> line,
	_ => catalogueName
};