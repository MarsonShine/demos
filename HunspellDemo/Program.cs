// See https://aka.ms/new-console-template for more information
using HunspellDemo;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

Console.WriteLine("Hello, World!");

// 假设词典在 Dictionaries 文件夹下
string affPath = "en_US.aff";
string dicPath = "en_US.dic";

// 加载词典
var dictionary = await WordList.CreateFromFilesAsync(dicPath, affPath);

string text = "Robin, where is the museum shop? I want to buy a colorful postcrad.";

// 用正则找单词及其位置
var wordRegex = new Regex(@"\b[\w']+\b");
var matches = wordRegex.Matches(text);

var errors = new List<SpellError>();

foreach (Match match in matches)
{
    string word = match.Value;
    int start = match.Index;
    int end = match.Index + match.Length;

    if (!dictionary.Check(word))
    {
        var suggestions = dictionary.Suggest(word).Take(5).ToList();
        errors.Add(new SpellError
        {
            Word = word,
            StartIndex = start,
            EndIndex = end,
            Suggestions = suggestions
        });
    }
}

// 打印结果
foreach (var error in errors)
{
    Console.WriteLine($"错误单词: {error.Word}，起止位置: [{error.StartIndex},{error.EndIndex})");
    Console.WriteLine($"建议: {string.Join(", ", error.Suggestions)}");
}
