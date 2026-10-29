# ProcessFileLineByLine.ps1 - MOVEit Automation PowerShell script template
# to process a file line-by-line, creating an output file that will replace
# the input file during the execution of the task.
#
# This template demonstrates:
# - Reading a file line-by-line in a memory-efficient way.
# - Writing a file line-by-line.
# - Replacing an input file with a modified version of that file.
#
# Make your changes after the "### Put your changes here" block.
try {
    # Open the file to be processed.
    $inputFilename = $miaclient.MICacheFilename()
    $inputReader = [System.IO.File]::OpenText($inputFilename)

    # Create a temporary file to hold the processed version of the input file.
    $outputFilename = $miaclient.MINewCacheFilename()
    $outputStream = [System.IO.StreamWriter] $outputFilename

    # Loop, reading lines from the input file and writing lines to the output file.
    for() {
        $line = $inputReader.ReadLine()
        if($line -eq $null) {
            break
        }
        ###
        ### Put your changes here.
        ### Set $changedLine to the processed version of the input line.
        ###
        $changedLine = $line
        # Write the altered line to the output file.
        $outputStream.WriteLine($changedLine)
    }

    # Close the input and output files.
    $inputReader.Close()
    $outputStream.Close()

    # Replace the input file with the output file, for task processing.
    $miaclient.MIReplaceCacheFile($outputFilename)
} catch {
    $miaclient.MILogMsg("Caught exception: $Error")
    $miaclient.MILogMsg($Error[0].ScriptStackTrace)
    # 10000 is a suggested error code for errors in user scripts.
    $miaclient.MISetErrorCode(10000)
    $miaclient.MISetErrorDescription("Error: " + $Error)
}
