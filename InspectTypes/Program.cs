using System;
using System.Reflection;
using System.Linq;

var asm = typeof(Polarion.PolarionClient).Assembly;

// Inspect PolarionClient
var clientType = asm.GetType("Polarion.PolarionClient");
if (clientType != null)
{
    Console.WriteLine("=== PolarionClient ===");
    foreach (var m in clientType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    {
        var parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine("  " + (m.IsStatic?"static ":"") + m.ReturnType.Name + " " + m.Name + "(" + parameters + ")");
    }
    Console.WriteLine();
    foreach (var p in clientType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine("  Prop: " + p.PropertyType.Name + " " + p.Name);
    Console.WriteLine();
}

// Inspect PolarionClientConfiguration
var configType = asm.GetType("Polarion.PolarionClientConfiguration");
if (configType != null)
{
    Console.WriteLine("=== PolarionClientConfiguration ===");
    foreach (var c in configType.GetConstructors())
    {
        var parameters = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine("  ctor(" + parameters + ")");
    }
    foreach (var p in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine("  Prop: " + p.PropertyType.Name + " " + p.Name);
    Console.WriteLine();
}

// Look for any token/PAT related types
Console.WriteLine("=== Types containing Token/Session/Auth ===");
foreach (var t in asm.GetTypes().Where(t => t.IsPublic && (t.Name.Contains("Token") || t.Name.Contains("Session") || t.Name.Contains("Auth"))))
    Console.WriteLine("  " + t.FullName);
Console.WriteLine();

// Check SessionWebService
Console.WriteLine("=== Types with Session in name ===");
foreach (var t in asm.GetTypes().Where(t => t.Name.Contains("Session")))
{
    Console.WriteLine("  " + t.FullName);
    if (t.Name == "SessionWebService")
    {
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
            Console.WriteLine("    " + m.ReturnType.Name + " " + m.Name + "(" + parameters + ")");
        }
    }
}

// Check logIn methods
Console.WriteLine();
Console.WriteLine("=== Methods containing logIn or Login ===");
foreach (var t in asm.GetTypes())
{
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name.IndexOf("login", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("logIn", StringComparison.OrdinalIgnoreCase) >= 0))
    {
        var parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine("  " + t.Name + "." + m.Name + "(" + parameters + ")");
    }
}

// Check for token-related methods
Console.WriteLine();
Console.WriteLine("=== Methods containing token ===");
foreach (var t in asm.GetTypes())
{
    foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => m.Name.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0))
    {
        var parameters = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine("  " + t.Name + "." + m.Name + "(" + parameters + ")");
    }
}
