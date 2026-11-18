Set env = CreateObject("WScript.Shell").Environment("PROCESS")
Set http = CreateObject("MSXML2.ServerXMLHTTP.6.0")
http.Open "POST", env("PEACH_API") & "/log", False
http.SetRequestHeader "Authorization", "Bearer " & env("PEACH_TOKEN")
http.Send "{""message"":""Hello from VBScript!""}"
WScript.Echo http.ResponseText
