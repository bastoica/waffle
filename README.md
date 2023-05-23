# WAFFLE

WAFFLE is an active delay injection tool for .NET platforms that aims to find memory ordering bugs - or MemOrder bugs, for short - such as use-after-free and use-before-init faults. 

This repository contains source code, benchmarks and scripts to build and reproduce the key findings of our <a href="https://bastoica.github.io/files/papers/2023_eurosys_waffle.pdf" target="_blank">paper</a> [1]. It also serves as a roadmap for applying WAFFLE to other compatible .NET applications. A great starting point is the walkthrough of instrumenting Ssh.Net in the ``Minimal Working Example'' section.



## Repository Structure

The artifact consists of 4 main directories:

* `Benchmarks`: contains the applications used to test Wafl, each in its own sub-directory

* `Code`: contains the source code of our tool

* `Configs`: contains configuration files to bootstrap the PowerShell scripts that drive each experiment

* `Scripts`: contains PowerShell and Python scripts used to build, run, and recreate the key experiments described in the paper



## System Requirements

WAFFLE is a tool designed for .NET platforms running on Windows (10.0.19042 or newer). It requires Visual Studio (2019 or newer), .NET 4.5 and Python >=3.10 (note: some of the benchmarks require different .NET frameworks, ranging from 3.5 to 4.7; instructions about these specific requirements are provided in later in the `README`). Ideally, WAFFLE would run on a machine with a 16-core CPU and 32 GB of RAM and 100+ GB disk space.



## Basic System Configurations

Before building and running WAFFLE or its benchmarks, users need to add several directory paths to the `PATH` system environment variable:


* vstest:  `C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\Common7\IDE\CommonExtensions\Microsoft\TestWindow`

* dotnet:  `C:\Program Files\dotnet`

* msbuild: `C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin`

* python:  `C:\Users\[USER_NAME]\AppData\Local\Programs\Python\Python310`

* .NET:  installing the benchmarks require the following .NET versions:
  * .NET Core 2.1+: https://dotnet.microsoft.com/en-us/download/dotnet/2.1
  * .NET Core 3.1+: https://dotnet.microsoft.com/en-us/download/dotnet/3.1
  * .NET Framework 4.5 (runtime): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net45
  * .NET Framework 4.5.1 (runtime + developmer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net451
  * .NET Framework 4.5.2 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net452
  * .NET Framework 4.6 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net46
  * .NET Framework 4.6.1 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net461
  * .NET Framework 4.6.2 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net462
  * .NET Framework 4.7 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net47
  * .NET Framework 4.7.1 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net471
  * .NET Framework 4.7.2 (runtime + developer pack): https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472


## Build Instructions 

### Building WAFFLE

One way to build WAFFLE is by opening `[ROOT_PATH]\memorder\Wafl\Wafl.sln` with Visual Studio (v2019 recommended) and build the source code using the IDE. Note that in this case the build mode - `RELEASE` vs `DEBUG` - and preprocessor flags - e.g. controling the delay policy - need to be configured manually (see pictures below). The two most important preprocessor flags are `/D WaflPolicy` to enable WAFFLE's core delay injection algorithm and `/D WaflBasicPolicy` to enable the delay injection scheme sed by the state-of-the-art [2]. These should be set for the `TorchLiteRuntime` module. We further recommend users build WAFFLE in `DEBUG` mode first as it simplifies troubleshooting.

<img src="https://user-images.githubusercontent.com/19847109/187139570-504ee42b-d861-4183-8f0f-80afb9ff76cb.png" alt="Instrumenter Properties" width="225"/> <img src="https://user-images.githubusercontent.com/19847109/187139580-519f6113-0255-414c-9b31-bc69f42e2a8d.png" alt="Instrumenter Configs" width="225"/> <img src="https://user-images.githubusercontent.com/19847109/187139598-c4fa8c36-4de5-4ecc-a0c1-3c2f135386c1.png" alt="Building WAFFLE" width="225"/>



Alternatively, users can build WAFFLE using PowerShell:

```

dotnet msbuild /p:DefineConstants=WaflPolicy;TraceGen /p:Configuration=Debug

```

### Buidling Individual Benchmarks

Building and preparing a particular benchmark for `MEMORDER` bug analysis typically involves 3 steps:
* Install relevant .NET dependencies
* Remove all binary signatures
* Build the core library along with it's dependencies, and the unit test library containing the bug-triggering inputs

Below we provide high-level instructions for building each benchmark.

#### 1. SSH.NET

See instructions in the ''Minimal Working Example'' sub-section.

#### 2. FluentAssertions

FluentAssertions requires .NET 45 and .NET 47. First, open `Benchmarks/FluentAssertions/FluentAssertions.sln` in Visual Studio and remove the binary signaures for all projects, starting with the core project - `FluentAssertions` (right click on each project in the Solution Explorer tab), click on Properties, navigate to the Signing tab, and de-select the ''Sign the assembly'' option. Finally, build `Net47.Specs` using the UI (right click on the project in the Solution Explorer tab, and select the `build` or the `rebuild` option). Build for both `DEBUG` and `RELEASE`.

#### 3. ApplicationInsights

ApplicationInsights requires .NET 452 and .NET 46. First, open `Benchmarks/ApplicationInsights/src/Microsoft.ApplicationInsights.sln` in Visual Studio and remove the binary signaures for all projects, starting with the core project - `Microsoft.ApplicationInsights` (right click on each project in the Solution Explorer tab), click on Properties, navigate to the Signing tab, and de-select the ''Sign the assembly'' option. Finally, build `Microsoft.ApplicationInsights.Net45.Tests` using the UI (right click on the project in the Solution Explorer tab, and select the `build` or the `rebuild` option). Build for both `DEBUG` and `RELEASE`.

#### 4. NSubstitute

NSubstitute requires .NET 47. First, open `Benchmarks/NSubstitute/NSubstitute.sln` in Visual Studio and remove the binary signaures for all projects, starting with the core project - `NSubstitute` (right click on each project in the Solution Explorer tab), click on Properties, navigate to the Signing tab, and de-select the ''Sign the assembly'' option. Finally, build `NSubstitute.Acceptance.Specs` using the UI (right click on the project in the Solution Explorer tab, and select the `build` or the `rebuild` option). Build for both `DEBUG` and `RELEASE`.

#### 5. NetMQ

NetMQ requires .NET 47. First, open `Benchmarks/NetMQ/NetMQ.sln` in Visual Studio and remove the binary signaures for all projects, starting with the core project - `NetMQ` (right click on each project in the Solution Explorer tab), click on Properties, navigate to the Signing tab, and de-select the ''Sign the assembly'' option. Finally, build `NetMQ.Tests` using the UI (right click on the project in the Solution Explorer tab, and select the `build` or the `rebuild` option). Build for both `DEBUG` and `RELEASE`.

#### 6. MQTTnet

MQTTnet requires .NET 452 and .NET 461. First, open `Benchmarks/MQTTnet/MQTTnet.noUWP.sln` in Visual Studio and remove the binary signaures for all projects, starting with the core project - `NQTTnet` (right click on each project in the Solution Explorer tab), click on Properties, navigate to the Signing tab, and de-select the ''Sign the assembly'' option. Finally, build `NQTTnet.Tests` using the UI (right click on the project in the Solution Explorer tab, and select the `build` or the `rebuild` option). Build for both `DEBUG` and `RELEASE`.


## Running WAFFLE

The high-level workflow of WAFFLE is as follows:

1. Users run WAFFLE's instrumentation library against all binaries of a target application
2. Once instrumented, the target application can be utiliezed normally by, say, running its various test suites as originally intended
3. At run time, the instrumentation transfers control to WAFFLE's runtime library (periodically, based on the target's execution behavior) which performs the core delay injection, triggers potential MemOrder bugs and logs debugging information

### Master scripts

We provide 2 master scripts that performs the workflow above automatically - one for illustrating the bug coverave, and the other for performance measurements. For each scenario (correctness or performance) we assume the target application once built has a directory where all the binaries can be found and has a separate binary that drives the test suite. 

#### Correctness Measurements

The script driving the bug coverage experiments runs applications in `DEBUG` mode to allow applications to explicitly crash or thrown an exception when a MemOrder bug is triggered, as opposed to failing silently most of the time as it happens in `RELEASE` mode. This is not a fundamental limitation of WAFFLE (which verifies programatically if an MemOrder bug occurs). but rather to increase the confidence of evaluators that what our tool reports are actual bugs. It also simplifies troubleshooting.

To run the master PowerShell scripts for a particular target application, use the following command from a PowerShell terminal to triggering each bug

```
cd .\[ROOT_PATH]\memorder\Scripts
.\correctness_master.ps1 [policy] [benchmark-name] [relative-path-to-target-benchmark-root-directory] [name-of-core-binary] [name-of-test-suite-binary] 
```

For example,
```
cd .\[ROOT_PATH]\memorder\Scripts
.\correctness_master.ps1 wafl ssh.net [ROOT_PATH_TO_REPOSITORY_PARENT_DIR]\memorder Ssh.NET\src\Renci.SshNet.Tests\bin\Debug Renci.SshNet.dll Renci.SshNet.Tests.dll
```
would run the core delay injection algorithm on the `Ssh.NET` benchmark located in the `.\Benchmarks` sub-directory.

This will attempt to trigger each bug 50 times using WAFFLE's delay injection approach (see Section 4, [1]) and another 50 times using the current state-of-the-ard (see Section 3, [1]). The script generates human-readable logs located in `[path-to-target-bin-directory]`, but we provide a log-parsing script (see "Collecting Results" below) to summarize and display results in a format similar to Table 4 (p. 10, [1]).

Note that this step should take less than $2$ combined human and machine hours to set up, run, and collect results.

To recreate the experimental results from the original paper [1], users need to run `.\correctness_master.ps1` 3 times:
* First, with the `original` option to collect run time measurements for the uninstrumented version of the application
* Second, with the `wafl` option to collect bug-trigger and run time measurements for the application version instrumented to run the core delay injection strategy described in [1]
* Third, with the `waflbasic` option to collect bug-trigger and run time measurements for the application version instrumented to run the prior state-of-the-art delay injection strategy [2]

For the 2nd and 3rd step, users also need to re-compile `WAFFLE` using the approriate preprocessor flags for the `TorchLiteRuntime` project: `/D WaflPolicy` to run the core delay injection strategy (2nd step), and `/D WaflBasicPolicy` to run the prior state-of-the-art delay injection strategy [2], respectively.

##### 1. SSH.Net

To measure the original, un-instrumented running time of the target application, run:
```
.\correctness_master.ps1 original ssh.net Ssh.NET\src\Renci.SshNet.Tests\bin\Debug Renci.SshNet.dll Renci.SshNet.Tests.dll
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:
```
.\correctness_master.ps1 wafl ssh.net Ssh.NET\src\Renci.SshNet.Tests\bin\Debug Renci.SshNet.dll Renci.SshNet.Tests.dll
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic ssh.net Ssh.NET\src\Renci.SshNet.Tests\bin\Debug Renci.SshNet.dll Renci.SshNet.Tests.dll
```

##### 2. FluentAssertions

To measure the original, un-instrumented running time of the target application, run:
```
.\correctness_master.ps1 original fluentassertions FluentAssertions\Tests\Net47.Specs\bin\Debug\net47 FluentAssertions.dll FluentAssertions.Net47.Specs.dll
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:
```
.\correctness_master.ps1 wafl fluentassertions FluentAssertions\Tests\Net47.Specs\bin\Debug\net47 FluentAssertions.dll FluentAssertions.Net47.Specs.dll
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic fluentassertions FluentAssertions\Tests\Net47.Specs\bin\Debug\net47 FluentAssertions.dll FluentAssertions.Net47.Specs.dll
```

##### 3. ApplicationInsights

To measure the original, un-instrumented running time of the target application, run:
```
.\correctness_master.ps1 original applicationinsights ApplicationInsights\bin\Debug\Test\Microsoft.ApplicationInsights.Test\Net45\net45 Microsoft.ApplicationInsights.dll Microsoft.ApplicationInsights.Net45.Tests.dll
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:
```
.\correctness_master.ps1 wafl applicationinsights ApplicationInsights\bin\Debug\Test\Microsoft.ApplicationInsights.Test\Net45\net45 Microsoft.ApplicationInsights.dll Microsoft.ApplicationInsights.Net45.Tests.dll
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic applicationinsights ApplicationInsights\bin\Debug\Test\Microsoft.ApplicationInsights.Test\Net45\net45 Microsoft.ApplicationInsights.dll Microsoft.ApplicationInsights.Net45.Tests.dll
```

##### 4. NSubstitute

To measure the original, un-instrumented running time of the target application, run:

```
.\correctness_master.ps1 original nsubstitute NSubstitute\bin\Debug\NSubstitute.Acceptance.Specs\net46 NSubstitute.dll NSubstitute.Acceptance.Specs.dll
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:

```
.\correctness_master.ps1 wafl nsubstitute NSubstitute\bin\Debug\NSubstitute.Acceptance.Specs\net46 NSubstitute.dll NSubstitute.Acceptance.Specs.dll
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic nsubstitute NSubstitute\bin\Debug\NSubstitute.Acceptance.Specs\net46 NSubstitute.dll NSubstitute.Acceptance.Specs.dll
```

##### 5. NetMQ

To measure the original, un-instrumented running time of the target application, run:

```
.\correctness_master.ps1 original netmq NetMQ\NetMQ.Tests\bin\Debug\net47 NetMQ.dll NetMQ.Tests.dll
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:

```
.\correctness_master.ps1 wafl netmq NetMQ\NetMQ.Tests\bin\Debug\net47 NetMQ.dll NetMQ.Tests.dll
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic netmq NetMQ\NetMQ.Tests\bin\Debug\net47 NetMQ.dll NetMQ.Tests.dll
```

##### 6. MQTTnet

To measure the original, un-instrumented running time of the target application, run:
```
.\correctness_master.ps1 original mqttnet
```

To measure the bug-triggering capabilities and running time for `WAFFLE`'s original delay injection strategy, run:
```
.\correctness_master.ps1 wafl mqttnet
```

Finally, to measure the bug-triggering capabilities and running time when using the prior state-of-the-art delay injection strategy, run:
```
.\correctness_master.ps1 waflbasic mqttnet
```


#### Performance Measurements

Similar to the other master script, to run the master PowerShell scripts for a particular target application, use the following command from a PowerShell terminal to triggering each bug

```
.\[ROOT_PATH]\memorder\Scripts\performance_master.ps1 [path-to-target-bin-directory] [name-of-test-suite-binary] 
```


### Collecting results

We provide a unified PowerShell script to collect results and present them in a similar format as Table 4, 5, and 6 in [1]. 

```
.\[ROOT_PATH]\memorder\Scripts\collect_results.ps1 
```


# Minimal Working Example

## Ssh.NET

We provide the source code for the Ssh.NET benchmark in `./Benchmarks/Ssh.Net`. Code can also be checked out from: https://github.com/sshnet/SSH.NET/

### Building benchmark

The most convenient way to compile and build Ssh.NET is using Visual Studio by launching `[ROOT_PATH]\memorder\Benchmarks\Ssh.Net\src\Renci.SshNet.VS2017.sln`.

Before building, users need to disable binary/assembly signing (see pictures below). We recomend doing this for all assembly/projects, not just for `Renci.SshNet` and `Renci.SshNet.Tests`:

<img src="https://user-images.githubusercontent.com/19847109/187060924-f88f9388-8b31-4f90-bd89-18e782d8d39e.png" alt="Disable signature for Renci.SshNet #1" width="225"/> <img src="https://user-images.githubusercontent.com/19847109/187061067-0d83a26b-f636-4813-a5f3-f28854bff0eb.png" alt="Disable signature for Renci.SshNet #2" width="225"/> <img src="https://user-images.githubusercontent.com/19847109/187061070-bc03a7a7-3248-4a7e-bddb-9a516ed59d10.png" alt="Disable signature for Renci.SshNet.Tests" width="225"/>

Next, we suggest building WAFFLE in `DEBUG` mode to allow the application print debug information on the console. Note that building the test suite - `Renci.SshNet.Tests` - automaticlaly builds the main binary `Renci.SshNet`.

<img src="https://user-images.githubusercontent.com/19847109/187061104-3ab29a2e-6a13-4ae6-8289-e8d89b400ca3.png" alt="Set `DEBUG` mode for Renci.SshNet" width="225"/>  <img src="https://user-images.githubusercontent.com/19847109/187061071-3391a86d-7bf2-4374-be16-9ca9ab871dd9.png" alt="Set `DEBUG` mode for Renci.SshNet.Tests" width="225"/><img src="https://user-images.githubusercontent.com/19847109/187061098-5fee5871-1a40-4f56-b5f1-386a7f804907.png" alt="Build Renci.SshNet.Tests" width="225"/> <img src="https://user-images.githubusercontent.com/19847109/187061099-25ba75b2-e381-4a95-9898-b140ad7fbda1.png" alt="Check Renci.SshNet.Tests built successfully" width="225"/>

### Instrumenting benchmark

To instrument a target binary using WAFFLE can be done with `TorchLite.exe [binary]` command in PowerShell. For Ssh.NET, this translates to

```
cd [ROOT_PATH]\memorder\Benchmarks\Ssh.NET\src\Renci.SshNet.Tests\bin\Debug
[ROOT_PATH]\memorder\Code\TorchLite\bin\Debug\TorchLite.exe ..\Benchmarks\Ssh.NET\src\Renci.SshNet.Tests\bin\Debug\Renci.SshNet.dll
```

### Triggering MemOrder bugs

Before attempting to trigger MemOrder bugs, WAFFLE requires a preparation run to generate MemOrder bug candidates. Running the instrumented version once, generates a human-readable `Races.txt` file in the run directiory. Subsequent runs will attempt to trigger MemOrder bugs.

##### Bug \#80

To trigger the MemOrder bug \#80 that occurs when both the server and client disconnect concurrently, first generate MemOrder candidates unsing the following PowerShell commands:

```
cd [ROOT_PATH]\memorder\Benchmarks\Ssh.NET\src\Renci.SshNet.Tests\bin\Debug
vstest.console.exe Renci.SshNet.Tests.dll /Tests:SessionTest_Connected_ServerAndClientDisconnectRace
.\[ROOT_PATH]\memorder\Code\TraceAnalysis\bin\Debug\TraceAnalysis.exe .\Runtime.wfl "FindUniquePotentialRaces"
```

Then, start a detection run using the following PowerShell command: 
```
vstest.console.exe Renci.SshNet.Tests.dll /Tests:SessionTest_Connected_ServerAndClientDisconnectRace 2>&1 | tee TestResults.wfl
```

This command generates a `TestResults.wfl` file that captures crash information. While in most cases running the previous command once would cause the application to crash, under unfavorable external configurations users might need to run it an extra 2-3 times until the crash occurs.

##### Bug \#453

To trigger the MemOrder bug \#80 that occurs when both the server and client disconnect concurrently , first generate MemOrder candidates (using PowerShell):
```
cd [ROOT_PATH]\memorder\Benchmarks\Ssh.NET\src\Renci.SshNet.Tests\bin\Debug
vstest.console.exe Renci.SshNet.Tests.dll /Tests:Expect_Regex_RacesWithDispose
.\[ROOT_PATH]\memorder\Code\TraceAnalysis\bin\Debug\TraceAnalysis.exe .\Runtime.wfl "FindUniquePotentialRaces"
```

and then start a detection run (using PowerShell):
```
vstest.console.exe Renci.SshNet.Tests.dll /Tests:Expect_Regex_RacesWithDispose 2>&1 | tee TestResults.wfl
```

This command, too, generates a `TestResults.wfl` file that captures crash information. While in most cases running the previous command once would cause the application to crash, under unfavorable external configurations users might need to run it an extra 2-3 times until the crash occurs.

## References

[1] "WAFFLE: Exposing Memory Ordering Bugs Efficiently with Active Delay Injection". Bogdan Alexandru Stoica, Shan Lu, Madan Musuvathi, and Suman Nath. | EuroSys 2023 <a href="https://bastoica.github.io/files/papers/2023_eurosys_waffle.pdf" target="_blank">[pdf]</a>

[2] "Efficient and Scalable Thread-Safety Violation Detection --- Finding thousands of concurrency bugs during testing". Guangpu Li, Shan Lu, Madan Musuvathi, Suman Nath, and Rohan Padhye. SOSP 2019
