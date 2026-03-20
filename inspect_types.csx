using System;
using System.Reflection;
using System.IO;
using System.Linq;

var dir = Path.Combine(Environment.CurrentDirectory, "PolarionRemoteMcpServer", "bin", "Debug", "net9.0");
var asm = Assembly.LoadFrom(Path.Combine(dir, "Polarion.dll"));
var types = asm.GetTypes().Where(t => t.Name.Contains("Approval") || t.Name.Contains("approv", StringComparison.OrdinalIgnoreCase)).ToArray();
foreach(var t in types) {
    Console.WriteLine($"=== {t.FullName} ===");
    foreach(var p in t.GetProperties()) Console.WriteLine($"  Prop: {p.PropertyType.Name} {p.Name}");
    foreach(var f in t.GetFields()) Console.WriteLine($"  Field: {f.FieldType.Name} {f.Name}");
    Console.WriteLine();
}
// Also check WorkItem.approvals
var wiType = asm.GetType("Polarion.Generated.Tracker.WorkItem");
if(wiType != null) {
    var ap = wiType.GetProperty("approvals");
    if(ap != null) Console.WriteLine($"WorkItem.approvals: {ap.PropertyType.FullName}");
    var sap = wiType.GetProperty("secureApprovalCommentId");
    if(sap != null) Console.WriteLine($"WorkItem.secureApprovalCommentId: {sap.PropertyType.FullName}");
}
// Check TrackerWebService for approval methods  
var tsType = asm.GetTypes().FirstOrDefault(t => t.Name.Contains("TrackerWebService"));
if(tsType != null) {
    var methods = tsType.GetMethods().Where(m => m.Name.Contains("approv", StringComparison.OrdinalIgnoreCase)).ToArray();
    foreach(var m in methods) {
        Console.WriteLine($"TrackerWebService.{m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))}) -> {m.ReturnType.Name}");
    }
}
