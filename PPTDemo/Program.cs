using System;
using System.Diagnostics;
using System.Linq;

namespace Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            PPTHelper.Read();
            //var list = Enumerable.Range(0,100000).ToList();
            //var sw = Stopwatch.StartNew();
            //for (int i = 0; i < list.Count; i++)
            //{
            //    _ = list.Where(p => p < 500).Count() > 1000;
            //}
            //sw.Stop();
            //Console.WriteLine(sw.ElapsedMilliseconds+"ms");
            //sw.Restart();
            //for (int i = 0; i < list.Count; i++)
            //{
            //    _ = list.Count(p => p < 500) > 1000;
            //}
            //Console.WriteLine(sw.ElapsedMilliseconds + "ms");
            //Console.ReadLine();

            var v = """
asdasdasdasdasdasdasdasd"asdasdsa"adasdasdiasjdasd"dasdasdasd"
""";
        }
    }
}
