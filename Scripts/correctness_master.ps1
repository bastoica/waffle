########################## PARAMETERS
$policy = $args[0]
$benchmark = $args[1]
$rootDir = $PSScriptRoot + "\..\"
$targetPath = $rootDir + "\Benchmarks\" + $args[2]
$targetCoreBinary = $targetPath  + "\" + $args[3]
$targetTestBinary = $targetPath  + "\" + $args[4]
$verbose = $args[6]


########################## CONSTANTS
$experiment_trials = 15
$trigger_runs = 10 # <--- for performance reasons, run 10x15= 150 times first, instead of the original 50x15= 750
$timeout = 60

$vsTestExe = "vstest.console.exe"
$traceAnalysisExe = $rootDir + "\Wafl\TraceAnalysis\bin\Debug\TraceAnalysis.exe"
$instrumenterExe = $rootDir + "\Wafl\TorchLite\bin\Debug\TorchLite.exe"

$faultyTestsLog = $rootDir + "\Configs\" + $benchmark + "\Tests.faulty.txt"
$resultsDir = $rootDir + "\Results"


########################## INSTRUMENT TARGET BINARY
if ($policy -ne "original")
{
	$cmd = {
		Param([string]$exe,[string]$target,[string]$tn)
		& $exe $target
	}
	$job = Start-Job -ScriptBlock $cmd -ArgumentList $instrumenterExe,$targetCoreBinary,(Get-Location).Path
	Wait-Job $job 1>$null 2>$null
	Receive-Job -Job $job
	Remove-Job -Force $job 1>$null 2>$null
} 
else 
{
	$cmd = {
		Param([string]$dir)
		cd $dir
		if (Test-Path ".\TorchLiteRuntime.dll")
		{
			echo "[ERROR]: Binary is already instrumented. Remove parent '.\bin' directory and recompile before attempting to time the un-instrumnetend version"
		}
		exit -1
	}
	$job = Start-Job -ScriptBlock $cmd -ArgumentList $targetPath
	Wait-Job $job -Timeout $timeout 1>$null 2>$null
	Receive-Job -Job $job  | Tee-Object -Variable output
	Remove-Job -Force $job 1>$null 2>$null
	
	if ($output -match "[ERROR]")
	{
		exit -1
	}
	else
	{
		$trigger_runs = 10 # There is no need to run the un-instrumented version 15x50= 750 times. 
						   # Rather, 150 should be enough for an adequate running time measurements.
	}
	
}

########################## RUN FAULTY TESTS
foreach ($line in Get-Content $faultyTestsLog)
{	
	# Get test name
	$testList = $line.Split(".")
	$testName = $testList[$testList.Count-1]
	
	for ($trial =  1; $trial -le $experiment_trials; $trial++)
	{
		# Path config
		$resultsPath = $resultsDir + "\correctness\" + $benchmark + "\" + $testName + "\" + $policy + "\Trial-" + $trial
		$cmd = {
			Param([string]$resultsPath)
			& New-Item -ItemType Directory -Force -Path $resultsPath
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $resultsPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null
		
		# Cleanup
		$cmd = {
			Param([string]$resultsPath,[string]$dir)
			& New-Item -ItemType Directory -Force -Path $resultsPath
			cd $dir
			& Remove-Item "*.wfl"
		    if (Test-Path ".\Wafl.log")
			{
				& Remove-Item ".\Wafl.log"
			}
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $resultsPath,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null
		
		# Execute preparation run
		$cmd = {
			Param([string]$exe,[string]$tg,[string]$tn,[string]$dir)
			cd $dir
			Write-Output "`n`n=========" | tee Wafl.log
			Write-Output "@Run # 1" | tee -a Wafl.log
			Write-Output "=========`n" | tee -a Wafl.log
			& $exe $tg /Tests:$tn 2>&1 | tee -a Wafl.log
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $vsTestExe,$targetTestBinary,$testName,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null

		# Identify MemOrder candidates (if any)
		if ($policy -eq "wafl")
		{
			$cmd = {
				Param([string]$exe,[string]$dir)
				cd $dir
				& $exe .\Runtime.wfl "FindUniquePotentialRaces"
			}
			$job = Start-Job -ScriptBlock $cmd -ArgumentList $traceAnalysisExe,$targetPath
			Wait-Job $job -Timeout $timeout 1>$null 2>$null
			Receive-Job -Job $job
			Remove-Job -Force $job 1>$null 2>$null
		}
		
		$cmd = {
			Param([string]$verbose,[string]$resultsPath,[string]$dir)
			cd $dir
			if (Test-Path ".\Candidates.wfl")
			{
				& Copy-Item ".\Candidates.wfl" $resultsPath 2>&1 | tee -a Wafl.log
				if ($verbose -match "VERBOSE")
				{
					& Copy-Item ".\Runtime.wfl" $resultsPath 2>&1 | tee -a Wafl.log
				}
			}
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $verbose,$resultsPath,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null
	
			
		# Execute delay injection runs
		for ($iter =  2; $iter -le $trigger_runs; $iter++)
		{
			# Execute delay injection for test $tn
			$cmd = {
				Param([string]$exe,[string]$tg,[string]$tn,[string]$iter,[string]$dir)
				cd $dir
				Write-Output "`n`n=========" | tee -a Wafl.log
				Write-Output "@Run # $iter" | tee -a Wafl.log
				Write-Output "=========`n" | tee -a Wafl.log
				& vstest.console.exe $tg /Tests:$tn 2>&1 | tee -a Wafl.log
			}
			$job = Start-Job -ScriptBlock $cmd -ArgumentList $vsTestExe,$targetTestBinary,$testName,$iter,$targetPath
			Wait-Job $job -Timeout $timeout 1>$null 2>$null
			Receive-Job -Job $job 2>&1 | Tee-Object -Variable output
			Remove-Job -Force $job 1>$null 2>$null
			
			# Save log files to a unique directory		
			$cmd = {
				Param([string]$resultsPath,[string]$dir,[string]$verbose)
				$date = (get-date -format 'yyyyMMdd-hhmmss').ToString()
				
				cd $dir
				if (Test-Path "Probs.wfl")
				{
					$resultLog = $resultsPath + "\Probs-$date.wfl"
					& Copy-Item ".\Probs.wfl" $resultLog
				}
				if (Test-Path "Stats.wfl")
				{
					$resultLog = $resultsPath + "\Stats-$date.wfl"
					& Copy-Item ".\Stats.wfl" $resultLog
				}
				if ($verbose -match "VERBOSE")
				{
					if (Test-Path ".\Runtime.wfl")
					{
						$resultLog = $resultsPath + "\Runtime-$date.wfl"
						& Move-Item ".\Runtime.wfl" $resultLog
					}
				}
				
				sleep -s 1
			}
			$job = Start-Job -ScriptBlock $cmd -ArgumentList $resultsPath,$targetPath,$verbose
			Wait-Job $job -Timeout $timeout 1>$null 2>$null
			Remove-Job -Force $job 1>$null 2>$null
			
			if ($output -match "ObjectDisposedException" -or $output -match "NullReferenceException" -or $output -match "NetMQ.FaultException : Cannot close an uninitialised Msg")
			{
				break
			}
		}
	
		# Move results log to a unique directory
		$cmd = {
			Param([string]$resultsPath,[string]$dir)
			cd $dir
			& Remove-Item "*.wfl"
		    if (Test-Path ".\Wafl.log")
			{
				& Move-Item ".\Wafl.log" $resultsPath
			}
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $resultsPath,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null		
	}
}
