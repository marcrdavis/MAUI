# MOVEitTransferRESTAPI.ps1 - MOVEit Automation PowerShell script template
# to retrieve the upload stamp of a MOVEit Transfer file.
# This template demonstrates:
# - Use of a REST API
# - Authentication via a token
# - Looping through paginated responses
#
# This example gets its configuration from task parameters, but you could
# also hard-code these items here in the script.
# The input task parameters are:
# - REST_Hostname: the hostname of the MOVEit Transfer server
# - REST_IgnoreCertErrors: "true" (not case-sensitive) if you want to ignore
#      certificate trust problems with the MOVEit Transfer server's certificate.
# - REST_Username: the username for authenticating to MOVEit Transfer.
# - REST_Password: the password for authenticating to MOVEit Transfer.
# - REST_FullFilename: the full path of the file whose upload stamp we want.
#      For example:  "/Home/fred/Report01.txt"
#
# Output task parameters:
# - REST_Uploadstamp:  Upload date/time of the file, or empty if not found.

try {
    # Fetch the task parameters.
    $hostname = $miaclient.MIGetTaskParam("REST_Hostname")
    $ignoreCertErrors = $miaclient.MIGetTaskParam("REST_IgnoreCertErrors")
    $username = $miaclient.MIGetTaskParam("REST_Username")
    $password = $miaclient.MIGetTaskParam("REST_Password")
    $pathSought = $miaclient.MIGetTaskParam("REST_FullFilename")

    # Start with empty found date stamp, in case of failure.
    $miaclient.MISetTaskParam("REST_Uploadstamp", "")

    # Compute the URL to which we will POST to obtain the access token.
    $url = "https://$hostname/api/v1/token"
    # Compute the body of the POST.
    $body = @{"grant_type" = "password"; "username" = $username; "password" = $password}

    if($ignoreCertErrors.ToLower() -eq "true") {
        # Ignore trust problems with the server certificate:
        $miaclient.MILogMsg("Ignoring certificate trust issues")
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    }

    $miaclient.MILogMsg("About to call $url to get access token")
    # This user agent string overrides the default PowerShell agent string:
    $useragent = "userscript 1.0"
    # Make the REST request to get the access token that will be used below.
    $resp = Invoke-RestMethod -Uri $url -Body $body -Method Post -UserAgent $useragent
    # Invoke-RestMethod has already parsed the JSON response into an object, so
    # to get the access token, all we have to do is reference the correct property name.
    $accesstoken = $resp.access_token

    # Debug print the first few characters of the access token.
    $miaclient.MILogMsg("Got token starting " + $accesstoken.Substring(0, 10) + "...")

    # Now that we are authenticated, we make REST calls to list the available files.
    # We examine the output, looking for a file object with the requested filename,
    # and if we find it, we extract its upload stamp and quit.
    #
    # MOVEit Transfer "paginates" its results:  it returns only a limited number of files
    # for each request.  Thus, we may need to make multiple requests before we find the
    # file in question.
    $requestPage = 1
    $requestPerPage = 20
    $uploadStamp = ""

    do {
        $miaclient.MILogMsg("Requesting page $requestPage")
        $url = "https://$hostname/api/v1/files?request.page=$requestPage&request.perPage=$requestPerPage"
        # We present the authentication token by including it in an Authorization header.
        $headers = @{"Authorization" = "Bearer $accesstoken"}
        $resp = Invoke-RestMethod -Uri $url -Method Get -UserAgent $useragent -Headers $headers
        $currentPage = $resp.paging.page
        $totalItems = $resp.paging.totalItems
        foreach($file in $resp.items) {
            if($file.path -eq $pathSought) {
                # We found the file path we are looking for.  Grab the stamp and break out.
                $uploadStamp = $file.uploadStamp
                break
            }
        }
        $requestPage++
        # Continue to loop if we haven't found the file and we haven't
        # yet gotten all available pages of output.
    } while($uploadStamp -eq "" -and $currentPage * $requestPerPage -lt $totalItems)
    if($uploadStamp -ne "") {
        $miaclient.MILogMsg("Upload stamp is $uploadStamp")
        # Return the result to the calling task.
        $miaclient.MISetTaskParam("REST_Uploadstamp", $uploadStamp)
    } else {
        $miaclient.MILogMsg("Could not find file $pathSought")
    }

} catch {
    $miaclient.MILogMsg("Caught exception: $Error")
    $miaclient.MILogMsg($Error[0].ScriptStackTrace)
    # 10000 is a suggested error code for errors in user scripts.
    $miaclient.MISetErrorCode(10000)
    $miaclient.MISetErrorDescription("Error: " + $Error)
}
