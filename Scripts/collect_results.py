import os
import sys

_TEST_NAME_TO_BUG_NUMBER = {
	"sshnet@SessionTest_Connected_ServerAndClientDisconnectRace": "Bug-1",
	"sshnet@Expect_Regex_RacesWithDispose": "Bug-2",
	"nsubstitute@Issue205": "Bug-3",
	"nsubstitute@Issue573": "Bug-4",
	"fluentassertions@Issue664": "Bug-6",
	"fluentassertions@Issue862": "Bug-7",
	"netmq@Issue814": "Bug-11",
	"netmq@EnqueueShouldNotBlockWhenCapacityIsZero": "Bug-15",
	"applicationinsights@Issue1106": "Bug-10",
	"applicationinsights@SendingLogicMarkedAsInternalSdkOperation": "Bug-14",
	"kubernetes@TestWatchWithHandlers": "Bug-9",
	"kubernetes@SendDataRemoteCommand": "Bug-18",
	"mqttnet@Will_Message_Send_Race": "Bug-16",
	"mqttnet@Manage_Session_MaxParallel_Subscribe": "Bug-17",
}

_TEST_NAME_TO_ID_MAP = {
	"sshnet@SessionTest_Connected_ServerAndClientDisconnectRace": 80,
	"sshnet@Expect_Regex_RacesWithDispose": 453,
	"nsubstitute@Issue205": 205,
	"nsubstitute@Issue573": 573,
	"fluentassertions@Issue664": 664,
	"fluentassertions@Issue862": 862,
	"netmq@Issue814": 814,
	"netmq@EnqueueShouldNotBlockWhenCapacityIsZero": 975,
	"applicationinsights@Issue1106": 1106,
	"applicationinsights@SendingLogicMarkedAsInternalSdkOperation": 2261,
	"kubernetes@TestWatchWithHandlers": 360,
	"kubernetes@SendDataRemoteCommand": -1,		
	"mqttnet@Will_Message_Send_Race": 1187,
	"mqttnet@Manage_Session_MaxParallel_Subscribe": 1188,
}

CORRECTNESS = {}
PERFORMANCE = [{}, {}]

_TIME_STATS_INDEX = 0
_DELAY_STATS_INDEX = 1

_INFINITY = 999999999

class BugTriggerResultsRow:
	def __init__(
		self, 
		waflAttempts, 
		waflTime, 
		waflbasicAttempts, 
		waflbasicTime, 
		originalTime
	) -> None:
		self.waflAttempts = waflAttempts
		self.waflTime = waflTime
		self.waflbasicAttempts = waflbasicAttempts
		self.waflbasicTime = waflbasicTime
		self.originalTime = originalTime

class PerformancePerPolicyRow:
	def __init__(
		self, 
		timeFirstRun, 
		timeSecondRun, 
		delayCount, 
		delayAmount
	) -> None:
		self.timeFirstRun = timeFirstRun
		self.timeSecondRun = timeSecondRun
		self.delayCount = delayCount
		self.delayAmount = delayAmount

class TimeStatsResultsRow:
	def __init__(
		self, 
		originalTimeFirstRun, 
		originalTimeSecondRun, 
		waflbasicTimeFirstRun, 
		waflbasicTimeSecondRun, 
		waflTimeFirstRun, 
		waflTimeSecondRun
	) -> None:
		self.originalTimeFirstRun = originalTimeFirstRun
		self.originalTimeSecondRun = originalTimeSecondRun
		self.waflbasicTimeFirstRun = waflbasicTimeFirstRun
		self.waflbasicTimeSecondRun = waflbasicTimeSecondRun
		self.waflTimeFirstRun = waflTimeFirstRun
		self.waflTimeSecondRun = waflTimeSecondRun
		
class DelayStatsResultsRow:
	def __init__(
		self, 
		waflbasicDelayCount, 
		waflbasicDelayAmount, 
		waflDelayCount, 
		waflDelayAmount
	) -> None:
		self.waflbasicDelayCount = waflbasicDelayCount
		self.waflbasicDelayAmount = waflbasicDelayAmount
		self.waflDelayCount = waflDelayCount
		self.waflDelayAmount = waflDelayAmount


def _ParseSuccessLog(log: str) -> (int, float):
	"""
	Parses 'Wafl.log' for a successful run. The function 
	assumes log files have the following format:
		
		@Run # [N]
		...
		Passed ... [BENCHMARK_NAME].[TEST_NAME]
		...
		Test Run Succesful.
		Total tests: 1
		     Passed: 1
         Total time: [X.Y] Seconds
	"""
	
	file = open(log, 'r', encoding="utf-16")
	lines = file.readlines()

	time = 0.0
	runs = 0
	for line in lines:
		if "Total time" in line:
			time += float(line.strip().split(" ")[2])
			runs += 1
			
	return(0, float((1.0 * time) / runs))


def _ParseFailureLog(log: str, policy: str) -> (int, float):
	"""
	Parses 'Wafl.log' for a failed run. The function 
	assumes log files have the following format:
		
		@Run # [N]
		...
		vstest.console.exe ... [BENCHMARK_NAME].[TEST_NAME] \[FAIL\]
		...
		Test Run Failed.
		Total tests: 1
		     Failed: 1
         Total time: [X.Y] Seconds
		 
	For timing measurements, we add the running time of the 1st run (preparation),
	and the running time for the failing run.
	"""
	
	file = open(log, 'r', encoding="utf-16")
	lines = file.readlines()

	run_iter = 0
	attempts = _INFINITY
	timeFailure = 0.0
	foundCrash = False
	for line in lines:	
		if "@Run #" in line:
			run_iter = int(line.strip().split(" ")[2])
			if foundCrash:
				break

		for exception in [
		  "ObjectDisposedException", 
		  "NullReferenceException", 
		  "NetMQ.FaultException : Cannot close an uninitialised Msg",
		  "Nullable object must have a value",
		]:
			if exception in line:
				attempts = run_iter
				foundCrash = True
				
		if foundCrash == True and "Total time" in line:
			timeFailure = float(line.strip().split(" ")[2])

	prepRun = False
	timePrep = 0.0
	for line in lines:
		if policy == "wafl" and line.strip() == "@Run # 1":
			prepRun = True
		elif policy == "waflbasic" and line.strip() == f"@Run # {attempts-1}":
			prepRun = True
		else:
			pass
			
		if "Total time" in line:
			timePrep += float(line.strip().split(" ")[2])
			if prepRun:
				break

	if timePrep > 0.0 and timeFailure > 0.0:
		time = timePrep + timeFailure
	else:
		time = 0.0
		
	return(attempts, time)

def GetCorrectnessResults(rootPath: str) -> None:
	"""
	Iterates over Wafl.log files for each delay injection policy, 
	i.e., original (or un-instrumented), wafl, and waflbasic.
	
	Note that the parsing function assumes the following directory path:
	
		[ROOT_PATH]\Results\correctness\[BENCHMARK_NAME]\[TEST_NAME]\[POLICY_TYPE]\TRIAL-[XYZ]
	
	and that 'Wafl.log' is present in that particular directory.
	"""
	
	correctnessDirPath = rootPath + "\\Results\\correctness"
	benchmarks = [ f.name for f in os.scandir(correctnessDirPath) if f.is_dir() ]
	for benchmark in benchmarks:
		bugPath = correctnessDirPath + "\\" + benchmark
		bugs = [ f.name for f in os.scandir(bugPath) if f.is_dir() ]
		for bug in bugs:
			perPolicyResults = {}
			
			for policy in ["original", "wafl", "waflbasic"]:
				policyPath = bugPath + "\\" + bug + "\\" + policy
				if (os.path.exists(policyPath) and os.path.isdir(policyPath)):	
					perTrialResults = []
					
					trials = [ f.name for f in os.scandir(policyPath) if f.is_dir() ]
					for trial in trials:
						logFile = policyPath + "\\" + trial + "\\" + "Wafl.log"
						triggerAttempts = _INFINITY
						triggerTime = 0.0
						if (os.path.exists(logFile)):
							if (policy == "original"):
								triggerAttempts, triggerTime = _ParseSuccessLog(logFile)
							else:
								triggerAttempts, triggerTime = _ParseFailureLog(logFile, policy)
						perTrialResults.append((triggerAttempts, triggerTime))
						
					perTrialResults = sorted(perTrialResults)
					perPolicyResults[policy] = (perTrialResults[int(len(perTrialResults)/2)])
				
				else:
					perPolicyResults[policy] = (_INFINITY, 0.0)
				
			waflAttempts, waflTime = perPolicyResults["wafl"]
			waflbasicAttempts, waflbasicTime = perPolicyResults["waflbasic"]
			_, originalTime = perPolicyResults["original"]
			
			CORRECTNESS[benchmark + "@" + bug] = BugTriggerResultsRow(
				waflAttempts, 
				waflTime, 
				waflbasicAttempts, 
				waflbasicTime, 
				originalTime
			)

def GenerateCorrectnessTable() -> None:
	"""
	Saves results in markdown table format and prints to console.
	"""
	
	columns = [
		"  No   ", 
		"     Application     ", 
		"  ID  ", 
		" Original-time (ms) ", 
		" WaflBasic-runs ", 
		" Wafl-runs ", 
		" WaflBasic-slowdown (x) ", 
		" Wafl-slowdown (x) "
	]
	
	table = []
	for key in CORRECTNESS.keys():
		bugNumber = _TEST_NAME_TO_BUG_NUMBER[key]
		benchmark = key.split("@")[0]
		bugGithubId = _TEST_NAME_TO_ID_MAP[key]
		if bugGithubId <= 0:
			bugGithubId = "n/a"
			
		if CORRECTNESS[key].originalTime > 0.0:
			originalTime = str(int(1000.0 * CORRECTNESS[key].originalTime))
		else:
			originalTime = "n/a"
		if CORRECTNESS[key].waflbasicAttempts < _INFINITY:
			waflbasicAttempts = str(CORRECTNESS[key].waflbasicAttempts)
		else:
			waflbasicAttempts = "n/a"
		if CORRECTNESS[key].waflAttempts < _INFINITY:
			waflAttempts = str(CORRECTNESS[key].waflAttempts)
		else:
			waflAttempts = "n/a"	
		if CORRECTNESS[key].originalTime > 0.0 and CORRECTNESS[key].waflbasicTime > 0.0:
			waflbasicSlowdown = str(round(float(1.0 * CORRECTNESS[key].waflbasicTime / CORRECTNESS[key].originalTime), 1))
		else:
			waflbasicSlowdown = "n/a"
		if CORRECTNESS[key].originalTime > 0.0 and CORRECTNESS[key].waflTime > 0.0:
			waflSlowdown = str(round(float(1.0 * CORRECTNESS[key].waflTime / CORRECTNESS[key].originalTime), 1))
		else:
			waflSlowdown = "n/a"
		
		table.append(
			"|" + 
			str(bugNumber).rjust(len(columns[0]), " ") + " |" + 
			str(benchmark).rjust(len(columns[1]), " ") + " |" + 
			str(bugGithubId).rjust(len(columns[2]), " ") + " |" + 
			str(originalTime).rjust(len(columns[3]), " ") + " |" +
			str(waflbasicAttempts).rjust(len(columns[4]), " ") + " |" +
			str(waflAttempts).rjust(len(columns[5]), " ") + " |" +
			str(waflbasicSlowdown).rjust(len(columns[6]), " ") + " |" +
			str(waflSlowdown).rjust(len(columns[7]), " ") + " |"
		)
	
	table = sorted(table, key = lambda x: (len(x.split("|")[1]), x.split("|")[1], x))
	
	header = "|"
	for col in columns:
		header += col + " |"
	table.insert(0, header)
	
	separator = "|"
	for col in columns:
		separator += ''.join([ '-' * (len(col)) ]) + "-|"
	table.insert(1, separator)
	
	for row in table:
		print(row)

def _ParseTimeStatsLog(log: str) -> (int, float):
	"""
	Parses 'Wafl.log' for a successful run. The function 
	assumes log files have the following format:
		
		@Run # [N]
		...
		Passed ... [BENCHMARK_NAME].[TEST_NAME]
		...
		Test Run Succesful.
		Total tests: 1
		     Passed: 1
         Total time: [X.Y] Seconds
	"""
	
	file = open(log, 'r', encoding="utf-16")
	lines = file.readlines()

	timeFirstRun = 0.0
	timeSecondRun = 0.0
	runs = 0
	for line in lines:
		if "Total time" in line:
			time = float(line.strip().split(" ")[2])
			runs += 1
		if runs == 1:
			timeFirstRun = time
		elif runs == 2:
			timeSecondRun = time
		else:
			pass
	
	if runs == 2:
		return(timeFirstRun, timeSecondRun)
	else:
		return (0.0, 0.0)
		
def _ParseDelayStatsLog(log: str) -> (int, float):
	"""
	Parses 'Stats-[TIMESTAMP].log' for a successful run. The function 
	assumes log files have the following format:
		
		#(0)TotalDelayMs	(1)TotalDelayCount
		12345				12345
	
	"""
	
	file = open(log, 'r', encoding="utf-8")
	lines = file.readlines()

	delayCount = 0
	delayAmount = 0
	for line in lines:
		if "TotalDelayMs" in line:
			continue
		delayAmount = int(line.strip().split("\t")[0])
		delayCount = int(line.strip().split("\t")[1])
	
	if delayCount > 0 and delayAmount > 0:
		return(delayCount, delayAmount)
	else:
		return (0, 0)

def GetPerformanceResults(rootPath: str) -> None:
	"""
	Iterates over Wafl.log and Stats.log files for each delay injection policy, 
	i.e., original (or un-instrumented), wafl, and waflbasic.
	
	Note that the parsing function assumes the following directory path:
	
		[ROOT_PATH]\Results\correctness\[BENCHMARK_NAME]\[TEST_NAME]\[POLICY_TYPE]\TRIAL-[XYZ]
	
	and that 'Wafl.log' and 'Stats-[TIMESTAMP].log' is present in that particular directory.
	"""
	
	correctnessDirPath = rootPath + "\\Results\\performance"
	benchmarks = [ f.name for f in os.scandir(correctnessDirPath) if f.is_dir() ]
	for benchmark in benchmarks:
		bugPath = correctnessDirPath + "\\" + benchmark
		bugs = [ f.name for f in os.scandir(bugPath) if f.is_dir() ]
		
		passed = 0
		
		originalTimeFirstRun = 0.0
		originalTimeSecondRun = 0.0
		waflbasicTimeFirstRun = 0.0
		waflbasicTimeSecondRun = 0.0
		waflTimeFirstRun = 0.0
		waflTimeSecondRun = 0.0
		
		waflbasicDelayCount = 0
		waflbasicDelayAmount = 0
		waflDelayCount = 0
		waflDelayAmount = 0
		
		for bug in bugs:
			perPolicyResults = {}
			
			count = 0
			for policy in ["original", "wafl", "waflbasic"]:
				policyPath = bugPath + "\\" + bug + "\\" + policy
				if (os.path.exists(policyPath) and os.path.isdir(policyPath)):	
					count += 1
					
			if count == 3:
				for policy in ["original", "wafl", "waflbasic"]:
					policyPath = bugPath + "\\" + bug + "\\" + policy
					trials = [ f.name for f in os.scandir(policyPath) if f.is_dir() ]
					
					timeFirstRun = 0.0
					timeSecondRun = 0.0
					delayCount = 0
					delayAmount = 0
					
					for trial in trials:
						logFile = policyPath + "\\" + trial + "\\" + "Wafl.log"
						if os.path.exists(logFile):
							first, second = _ParseTimeStatsLog(logFile)
							timeFirstRun += first
							timeSecondRun += second
						
						files = [ f.name for f in os.scandir(os.path.join(policyPath,trial)) if f.is_file() ]
						for file in files:
							if "Stats-" in file:
								logFile = policyPath + "\\" + trial + "\\" + file
								count, amount = _ParseDelayStatsLog(logFile)
								delayCount += count
								delayAmount += amount
								

					timeFirstRun = timeFirstRun / (1.0 * len(trials))
					timeSecondRun = timeSecondRun / (1.0 * len(trials))
					delayCount = int(delayCount / len(trials))
					delayAmount = int(delayAmount / len(trials))

					perPolicyResults[policy] = PerformancePerPolicyRow(
						timeFirstRun, 
						timeSecondRun, 
						delayCount, 
						delayAmount
					)
					
				passed += 1
				
				originalTimeFirstRun += perPolicyResults["original"].timeFirstRun
				originalTimeSecondRun += perPolicyResults["original"].timeSecondRun
				waflbasicTimeFirstRun += perPolicyResults["waflbasic"].timeFirstRun
				waflbasicTimeSecondRun += perPolicyResults["waflbasic"].timeSecondRun
				waflTimeFirstRun += perPolicyResults["wafl"].timeFirstRun
				waflTimeSecondRun += perPolicyResults["wafl"].timeSecondRun
				
				waflbasicDelayCount += perPolicyResults["waflbasic"].delayCount
				waflbasicDelayAmount += perPolicyResults["waflbasic"].delayAmount
				waflDelayCount += perPolicyResults["wafl"].delayCount
				waflDelayAmount += perPolicyResults["wafl"].delayAmount
			
		if passed > 0:
			originalTimeFirstRun /= (1.0 * passed)
			originalTimeSecondRun /= (1.0 * passed)
			waflbasicTimeFirstRun /= (1.0 * passed)
			waflbasicTimeSecondRun /= (1.0 * passed)
			waflTimeFirstRun /= (1.0 * passed)
			waflTimeSecondRun /= (1.0 * passed)
		else:
			print("[ERROR] No data compiled for " + benchmark)
			continue
			
		PERFORMANCE[_TIME_STATS_INDEX][benchmark + "@" + bug] = TimeStatsResultsRow(
			originalTimeFirstRun,
			originalTimeSecondRun,
			waflbasicTimeFirstRun,
			waflbasicTimeSecondRun,
			waflTimeFirstRun,
			waflTimeSecondRun
		)
		
		PERFORMANCE[_DELAY_STATS_INDEX][benchmark + "@" + bug] = DelayStatsResultsRow(
			waflbasicDelayCount,
			waflbasicDelayAmount,
			waflDelayCount,
			waflDelayAmount
		)

def GeneratePerformanceTimeTable() -> None:
	"""
	Saves results in markdown table format and prints to console.
	"""
	
	columns = [
		"     Application     ", 
		"  Base (ms)  ", 
		" Wafldbasic-run1 (ms) ",
		" Wafldbasic-run2 (ms) ",		
		" Wafl-run1 (ms) ",
		" Wafl-run2 (ms) ",
	]
	
	table = []
	for key in PERFORMANCE[_TIME_STATS_INDEX].keys():
		benchmark = key.split("@")[0]
			
		if PERFORMANCE[_TIME_STATS_INDEX][key].originalTimeFirstRun > 0.0 and PERFORMANCE[_TIME_STATS_INDEX][key].originalTimeSecondRun > 0.0:
			originalBase = str(int(1000.0 * (PERFORMANCE[_TIME_STATS_INDEX][key].originalTimeFirstRun + PERFORMANCE[_TIME_STATS_INDEX][key].originalTimeSecondRun) / 2.0))
		else:
			originalBase = "n/a"
		if PERFORMANCE[_TIME_STATS_INDEX][key].waflbasicTimeFirstRun > 0.0 and PERFORMANCE[_TIME_STATS_INDEX][key].waflbasicTimeSecondRun > 0.0:
			waflbasicTimeFirstRun = str(int(1000.0 * PERFORMANCE[_TIME_STATS_INDEX][key].waflbasicTimeFirstRun))
			waflbasicTimeSecondRun = str(int(1000.0 * PERFORMANCE[_TIME_STATS_INDEX][key].waflbasicTimeSecondRun))
		else:
			waflbasicTimeFirstRun = "n/a"
			waflbasicTimeSecondRun = "n/a"
		if PERFORMANCE[_TIME_STATS_INDEX][key].waflTimeFirstRun > 0.0 and PERFORMANCE[_TIME_STATS_INDEX][key].waflTimeSecondRun > 0.0:
			waflTimeFirstRun = str(int(1000.0 * PERFORMANCE[_TIME_STATS_INDEX][key].waflTimeFirstRun))
			waflTimeSecondRun = str(int(1000.0 * PERFORMANCE[_TIME_STATS_INDEX][key].waflTimeSecondRun))
		else:
			waflTimeFirstRun = "n/a"
			waflTimeSecondRun = "n/a"
			
			
		table.append(
			"|" + 
			str(benchmark).rjust(len(columns[0]), " ") + " |" + 
			str(originalBase).rjust(len(columns[1]), " ") + " |" + 
			str(waflbasicTimeFirstRun).rjust(len(columns[2]), " ") + " |" + 
			str(waflbasicTimeSecondRun).rjust(len(columns[3]), " ") + " |" +
			str(waflTimeFirstRun).rjust(len(columns[4]), " ") + " |" +
			str(waflTimeSecondRun).rjust(len(columns[5]), " ") + " |"
		)
	
	table = sorted(table, key = lambda x: (len(x.split("|")[1]), x.split("|")[1], x))
	
	header = "|"
	for col in columns:
		header += col + " |"
	table.insert(0, header)
	
	separator = "|"
	for col in columns:
		separator += ''.join([ '-' * (len(col)) ]) + "-|"
	table.insert(1, separator)
	
	for row in table:
		print(row)


def GeneratePerformanceDeleyaCountTable() -> None:
	"""
	Saves results in markdown table format and prints to console.
	"""
	
	columns = [
		"     Application     ", 
		" Wafldbasic-#delays ",
		" Wafldbasic-duration (ms) ",		
		" Wafl-#delays ",
		" Wafl-duration (ms) ",
	]
	
	table = []
	for key in PERFORMANCE[_DELAY_STATS_INDEX].keys():
		benchmark = key.split("@")[0]
			
		if PERFORMANCE[_DELAY_STATS_INDEX][key].waflbasicDelayCount > 0 and PERFORMANCE[_DELAY_STATS_INDEX][key].waflbasicDelayAmount > 0:
			waflbasicDelayCount = PERFORMANCE[_DELAY_STATS_INDEX][key].waflbasicDelayCount
			waflbasicDelayAmount = PERFORMANCE[_DELAY_STATS_INDEX][key].waflbasicDelayAmount
		else:
			waflbasicDelayCount = "n/a"
			waflbasicDelayAmount = "n/a"
		if PERFORMANCE[_DELAY_STATS_INDEX][key].waflDelayCount > 0 and PERFORMANCE[_DELAY_STATS_INDEX][key].waflDelayAmount > 0:
			waflDelayCount = PERFORMANCE[_DELAY_STATS_INDEX][key].waflDelayCount
			waflDelayAmount = PERFORMANCE[_DELAY_STATS_INDEX][key].waflDelayAmount
		else:
			waflDelayCount = "n/a"
			waflDelayAmount = "n/a"
			
			
		table.append(
			"|" + 
			str(benchmark).rjust(len(columns[0]), " ") + " |" + 
			str(waflbasicDelayCount).rjust(len(columns[1]), " ") + " |" + 
			str(waflbasicDelayAmount).rjust(len(columns[2]), " ") + " |" +
			str(waflDelayCount).rjust(len(columns[3]), " ") + " |" +
			str(waflDelayAmount).rjust(len(columns[4]), " ") + " |"
		)
	
	table = sorted(table, key = lambda x: (len(x.split("|")[1]), x.split("|")[1], x))
	
	header = "|"
	for col in columns:
		header += col + " |"
	table.insert(0, header)
	
	separator = "|"
	for col in columns:
		separator += ''.join([ '-' * (len(col)) ]) + "-|"
	table.insert(1, separator)
	
	for row in table:
		print(row)
		
	
			
if __name__ == "__main__":
	
	if len(sys.argv) < 2:
		print("[ERROR]: The script takes two parameters $mode and $rootPath of the `.\Results` directory")
		exit(-1)
	
	mode = sys.argv[1]
	rootPath = os.getcwd() + "\\.."
	
	if mode == "correctness":
		GetCorrectnessResults(rootPath)
		GenerateCorrectnessTable()	
	elif (mode == "performance"):
		GetPerformanceResults(rootPath)
		print("\n\n\nRunning time Measurements (ms):\n===============================\n\n")
		GeneratePerformanceTimeTable()
		print("\n\n\nDelay Injection Statistics:\n===========================\n\n")
		GeneratePerformanceDeleyaCountTable()
	else:
		print("[ERROR]: Unknown parameter: $mode should be either 'correctness' or 'performance'")