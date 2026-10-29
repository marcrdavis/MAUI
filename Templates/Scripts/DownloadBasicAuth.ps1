# DownloadBasicAuth.ps1 - MOVEit Automation PowerShell script template
# to download a file, using Basic Authentication.
# This template demonstrates:
# - Authenticating to a website using Basic Authentication
# - Downloading a file from a website
# - Adding a file to the list of files to be processed by subsequent
#   task steps.
#
# This example gets its configuration from task parameters, but defaults
# to values for an Ipswitch samples website if not supplied.
# The input task parameters are:
# - REST_Url: the full URL of the filename to be downloaded, such as
#      https://myserver/mydir/myfile.dat
# - REST_Username: the username for authenticating to the webserver.
# - REST_Password: the password for authenticating to the webserver.

try {
    # Obtain the URL for the file.
    $url = $miaclient.MIGetTaskParam("REST_Url")
    # Provide a default value if none was supplied.
    if($url -eq "") {
      $url = "https://api.moveitsamples.com/apidemo1/v1/downloads/file1.bin"
    }
    # Compute the filename MOVEit Automation should use for subsequent processing.
    # This is simply everything after the last /.
    $filename = $url.Substring(1+$url.lastIndexOf("/"))
    # Get a scratch filename to use for the download.
    $cacheFilename = $miaclient.MINewCacheFilename()

    # Compute authentication information, in the form required by
    # Basic Authentication, namely base64(username:password).
    $username = $miaclient.MIGetTaskParam("REST_Username")
    # Provide a default value if none was supplied.
    if($username -eq "") {
        $username = "midemo2"
    }
    $password = $miaclient.MIGetTaskParam("REST_Password")
    # Provide a default value if none was supplied.
    if($password -eq "") {
        $password = "midemopw2"
    }
    $authenBare = [System.Text.Encoding]::UTF8.GetBytes($username +":" + $password)
    $authenBase64 = [System.Convert]::ToBase64String($authenBare)

    # Construct web client object and pass authentication info.
    $wc = New-Object System.Net.WebClient
    $wc.Headers.Add("Authorization", "Basic " + $authenBase64)

    # Do the actual download
    $miaclient.MILogMsg("Downloading from $url to $cacheFilename")
    $wc.DownloadFile($url, $cacheFilename)

    # Tell MIA to add this file to the list to be processed.
    $miaclient.MIAddFile($cacheFilename, $filename)
} catch {
    $miaclient.MILogMsg("Caught exception: $Error")
    $miaclient.MILogMsg($Error[0].ScriptStackTrace)
    # 10000 is a suggested error code for errors in user scripts.
    $miaclient.MISetErrorCode(10000)
    $miaclient.MISetErrorDescription("Error: " + $Error)
}
