namespace HunspellDemo
{
    public class SpellError
    {
        public string? Word { get; set; }
        public int StartIndex { get; set; }
        public int EndIndex { get; set; }
        public List<string>? Suggestions { get; set; }
    }
}
