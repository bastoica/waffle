import random
import sys

def main(input, output):
	with open(input, 'r') as inFile: 
		lines = list(inFile)
	with open(output, 'w') as outFile:
		for i in range(50): 
			outFile.write(random.choice(lines))
			
			
if __name__ == "__main__":
	if len(sys.argv) != 3:
		printf("[ERROR] This command takes 2 arguments: an input file and an output file")
		exit(-1)
	else:
		main(sys.argv[1], sys.argv[2])
	