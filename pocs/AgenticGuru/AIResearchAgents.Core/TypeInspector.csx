using System;
using System.Linq;
using System.Reflection;
using OpenAI.Responses;

var type = typeof(OpenAIResponse);
Console.WriteLine($"=== {type.FullName} ===");
foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
}

// Check OutputItem
var outputType = typeof(ResponseItem);
Console.WriteLine($"\n=== {outputType.FullName} ===");
foreach (var prop in outputType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
}

// Check ResponseContentPart
var partType = typeof(ResponseContentPart);
Console.WriteLine($"\n=== {partType.FullName} ===");
foreach (var prop in partType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
{
    Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name}");
}

// Check static methods
foreach (var method in partType.GetMethods(BindingFlags.Public | BindingFlags.Static))
{
    Console.WriteLine($"  static {method.ReturnType.Name} {method.Name}({string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");
}
