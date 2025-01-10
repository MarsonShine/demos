using ShapeCrawler;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Demo
{
    internal class PPTHelper
    {
        public static void Read()
        {
            using IPresentation presentation = SCPresentation.Open(@"xxx.pptx", isEditable: true);
            ISlide slide = presentation.Slides[1];
            // 获取slide背景图片
            var background = slide.Background;
            var shapes = slide.Shapes;

            // Get text holder auto shape
            //IAutoShape autoShape = (IAutoShape)slide.Shapes.First(sp => sp is IAutoShape);
            IAutoShape autoShape = (IAutoShape)shapes[1];

            // Change whole shape text
            autoShape.TextBox.Text = "A new shape text";

            // Change text for a certain paragraph
            IParagraph paragraph = autoShape.TextBox.Paragraphs[1];
            paragraph.Text = "A new text for second paragraph";

            // Get font name and size
            IPortion paragraphPortion = autoShape.TextBox.Paragraphs.First().Portions.First();
            Console.WriteLine($"Font name: {paragraphPortion.Font.Name}");
            Console.WriteLine($"Font size: {paragraphPortion.Font.Size}");

            // Set bold font
            paragraphPortion.Font.IsBold = true;

            // Get font ARGB color
            Color fontColor = paragraphPortion.Font.ColorFormat.Color;

            // Save changes presentation
            presentation.Save();
        }
    }
}
