# TorchLite
The goal of the TorchLite tool is to uncover order-violation bugs in .NET applications. Examples of order-violation bugs include the following scenarios:
```csharp
Thread1: List<int> list;
Thread2: list.Add(3); // use object before creation
Thread1: list = new List<int>();
```

```csharp
Thread1: list = null; // or object.Dispose();
Thread2: list.Add(3); // use object after assigning null, or being disposed
```
# How to use the tool
Given an assembly `foo.exe`, do the following:
- Use `TorchLite.exe foo.exe`. This will instrument the assembly.
- Run the instrumented `foo.exe`. This will create a runtime trace with name `Runtime.log`.
- Run `TraceAnalysis.exe Runtime.log`. This will analyze the trace to extract potential order violations and write them to a file named `races.txt`.
- Run the instrumented `foo.exe` again, with the file `races.txt` in the same directory. TorchLite runtime will read the `races.txt` file and try to force the order violations.

