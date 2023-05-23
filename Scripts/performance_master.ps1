########################## PARAMETERS
$policy = $args[0]
$benchmark = $args[1]
$rootDir = $PSScriptRoot + "\.."
$targetPath = $rootDir + "\Benchmarks\" + $args[2]
$targetCoreBinary = $targetPath  + "\" + $args[3]
$targetTestBinary = $targetPath  + "\" + $args[4]
$verbose = $args[6]


########################## CONSTANTS
$experiment_trials = 5 # <--- for performance reasons, run 10x15= 150 times first, instead of the original 50x15= 750
$timeout = 60

$vsTestExe = "vstest.console.exe"
$traceAnalysisExe = $rootDir + "\Wafl\TraceAnalysis\bin\Release\TraceAnalysis.exe"
$instrumenterExe = $rootDir + "\Wafl\TorchLite\bin\Release\TorchLite.exe"

$configDir = $rootDir + "\Configs\" + $benchmark
$unitTests = $configDir + "\Tests.sample.txt"
$resultsDir = $rootDir + "\Results"


########################## LIST TESTS
if (!(Test-Path $unitTests))
{
	# Path config
	$allTests = $configDir + "\Tests.multithreaded.txt"
	$genRandSampleScript = $rootDir + "\Scripts\GenerateRandomSample.py"
	$cmd = {
		Param([string]$genRandSampleScript,[string]$allTests,[string]$unitTests)
		& python $genRandSampleScript $allTests $unitTests
	}
	$job = Start-Job -ScriptBlock $cmd -ArgumentList $genRandSampleScript, $allTests,$unitTests
	Wait-Job $job -Timeout $timeout 1>$null 2>$null
	Receive-Job -Job $job
	Remove-Job -Force $job 1>$null 2>$null
}

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
}

########################## RUN UNIT TESTS
foreach ($line in Get-Content $unitTests)
{	
	# Get test name
	$testList = $line.Split(".")
	$testName = $testList[$testList.Count-1]
	
	for ($trial =  1; $trial -le $experiment_trials; $trial++)
	{
		# Path config
		$resultsPath = $resultsDir + "\performance\" + $benchmark + "\" + $testName + "\" + $policy + "\Trial-" + $trial
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
			Param([string]$dir)
			cd $dir
			& Remove-Item "*.wfl"
		    if (Test-Path ".\Wafl.log")
			{
				& Remove-Item ".\Wafl.log"
			}
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null
		
		# Execute preparation run (for WAFL)
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
		
		# Copy log files to a unique directory
		$cmd = {
			Param([string]$verbose,[string]$resultsPath,[string]$dir)
			
			$date = (get-date -format 'yyyyMMdd-hhmmss').ToString()
			
			cd $dir
			if (Test-Path ".\Candidates.wfl")
			{
				& Copy-Item ".\Candidates.wfl" $resultsPath 2>&1 | tee -a Wafl.log
				if ($verbose -match "VERBOSE")
				{
					& Copy-Item ".\Runtime.wfl" $resultsPath 2>&1 | tee -a Wafl.log
				}
			}
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
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $verbose,$resultsPath,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
		Remove-Job -Force $job 1>$null 2>$null
	
		# Execute delay injection runs
		$cmd = {
			Param([string]$exe,[string]$tg,[string]$tn,[string]$dir)
			cd $dir
			Write-Output "`n`n=========" | tee -a Wafl.log
			Write-Output "@Run # 2" | tee -a Wafl.log
			Write-Output "=========`n" | tee -a Wafl.log
			& vstest.console.exe $tg /Tests:$tn 2>&1 | tee -a Wafl.log
		}
		$job = Start-Job -ScriptBlock $cmd -ArgumentList $vsTestExe,$targetTestBinary,$testName,$targetPath
		Wait-Job $job -Timeout $timeout 1>$null 2>$null
		Receive-Job -Job $job
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
