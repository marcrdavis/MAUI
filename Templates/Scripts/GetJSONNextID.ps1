# GetJSONNextID.ps1 - MOVEit Automation PowerShell script template
# to POST a JSON request to an endpoint, and receive a JSON response.
# The request contains a region name whose "nextid" is being required.
# The response contains the "nextid" of the requested region.
#
# This template demonstrates:
# - Creating a JSON payload
# - POSTing the JSON payload to a website
# - Receiving a JSON response from the website
# - Parsing an element from the JSON response
#
# This example gets its configuration from task parameters, but defaults
# to values for an Ipswitch samples website if not supplied.
# The input task parameters are:
# REST_Url:     the full URL of the endpoint being accessed.
# REST_Region:  the name of the region whose "nextid" is being requested.
#               On the sample server, the only valid region names are
#               "east" and "west".
try {
    # Get the input parameters.
    $url = $miaclient.MIGetTaskParam("REST_Url")
    # Provide a default value if none was supplied.
    if($url -eq "") {
        $url = "https://api.moveitsamples.com/apidemo1/v1/nextid"
    }
    $region = $miaclient.MIGetTaskParam("REST_Region")
    # Provide a default value if none was supplied.
    if($region -eq "") {
        $region = "west"
    }
    # Construct the JSON body to POST.
    $body = @{"region" = $region} | ConvertTo-Json
    # Make the web request.
    $resp = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType 'application/json'
    # Retrieve the desired value from the JSON response.
    $nextid = $resp.nextid
    # Debug log it.
    $miaclient.MILogMsg("Next ID for region $region is $nextid")
    # Make the returned nextid available to the rest of the task via a parameter.
    $miaclient.MISetTaskParam("REST_Nextid", $nextid)
} catch {
    # Error handling.
    $miaclient.MILogMsg("Caught exception: $Error")
    $miaclient.MILogMsg($Error[0].ScriptStackTrace)
    # 10000 is a suggested error code for errors in user scripts.
    $miaclient.MISetErrorCode(10000)
    $miaclient.MISetErrorDescription("Error: " + $Error)
}
