using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using EasyDotnet;

namespace MyApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var test = new EasyDotnetCodeFixer();
            var list = new List<string>();
            var another = new ConcurrentDictionary<string, string>();
            Console.WriteLine("Hello!");
            var json = JsonSerializer.Serialize(list);
        }
    }
}

namespace EasyDotnet
{
    public class EasyDotnetCodeFixer
    {

    }
}