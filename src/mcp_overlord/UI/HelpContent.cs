namespace mcp_overlord.UI
{
    /// <summary>Help shown in the main window. Update this when the window changes.</summary>
    public static class HelpContent
    {
        /// <summary>Help text.</summary>
        public const string Text =
            "MCP Overlord hosts an MCP server so a client can see a desktop application and drive it.\r\n" +
            "The title bar shows the open configuration file. A new document is titled New. " +
            "When the program is running elevated, the title starts with Administrator: and the title bar is dark blue.\r\n" +
            "\r\n" +
            "New, Load, Save, and Save as\r\n" +
            "New starts an empty configuration. Load opens an Overlord configuration (.overlord). " +
            "The contents are JSON and schemaVersion must be 1. " +
            "A bad file leaves the previous configuration in place and lists every error in the log. " +
            "Save writes the open file. Save as asks for a path. A new document asks for a path the first time it is saved. " +
            "Saving into a different folder turns relative paths into absolute paths so they still point at the same files.\r\n" +
            "\r\n" +
            "Unsaved changes\r\n" +
            "A dot and the words Unsaved changes appear when the document differs from the last load or save. " +
            "Save is highlighted while there is something to write. Changing a value back clears the indicator. " +
            "Closing the window, New, and Load ask whether to save. Don't save drops the edits. Cancel stays on the current document.\r\n" +
            "\r\n" +
            "Applications\r\n" +
            "Add, Edit, Duplicate, and Remove change the application list. Double-click a row, or the pencil, to edit it. " +
            "OK applies the change immediately, even before the file is saved. Name, executable, and window title are required. " +
            "Parameters, working directory, process name, description, and click backend are saved only when Save is checked, including when the value is empty. " +
            "An empty optional value is treated as not set. Application changes apply immediately. " +
            "Port, certificate, bind address, and API key changes wait until the next start.\r\n" +
            "\r\n" +
            "Filling an application\r\n" +
            "Drop a shortcut (.lnk) or an executable (.exe) on the editor to fill the fields that file provides and check them. " +
            "That drop still works when this app is running as administrator. " +
            "The target sits to the right of the drop area, the same height as that row. Drag it onto another window, the way Process Explorer does, and release to read that window's title, executable, process name, working directory, and parameters. " +
            "Escape cancels the drag. The outline follows the window under the cursor.\r\n" +
            "\r\n" +
            "Settings\r\n" +
            "The gear button edits the server block: HTTP port, HTTPS port, HTTPS, bind address, API key, certificate, host names, and log retention. " +
            "Each value is written only when Save is checked. Generate creates an API key. Show reveals it. " +
            "Log retention is how many lines the window keeps. The default is 1,000 and the smallest value is 100. It applies when you press OK and does not restart the listener. " +
            "A bind address that is not loopback needs HTTPS and an API key before Start will listen. " +
            "Listener changes apply the next time the server starts.\r\n" +
            "\r\n" +
            "Check connection\r\n" +
            "The check button opens a report for every HTTP and HTTPS endpoint. " +
            "It looks at whether this process is running, whether the port is listening, whether the name resolves, whether the address belongs to this machine, whether TCP connects, and, for HTTPS, the certificate. " +
            "It then sends an MCP initialize and a tools list, including the API key when one is set. " +
            "Each line is PASS, WARN, or FAIL. " +
            "PASS means the step succeeded. WARN means the step finished and the detail needs attention. FAIL means the step did not succeed. " +
            "A failed step adds an insight: ping, a trace route of up to 20 hops, and a hint such as a stopped server, a closed port, a refused or timed-out connection, a public address that is not on this machine, a rejected API key, a rejected Origin, or a certificate that is untrusted and was signed by someone else. " +
            "A self-signed certificate is WARN when the handshake completes and the certificate name matches the host, including when this machine already trusts it. " +
            "Remote clients still have to trust it themselves. " +
            "The insight says that no certificate authority vouches for it, so a client rejects the connection until the certificate is in that client's Trusted Root Certification Authorities store. " +
            "Until a client trusts it, TLS cannot prove the identity of the server, and the API key could be intercepted. " +
            "When the thumbprint matches the certificate this server created in %LOCALAPPDATA%\\mcp_overlord, the insight says the certificate was created because no certificate path is configured, and it points to the Signed certificate section. " +
            "A self-signed certificate loaded from a configured file gets the same warning with that origin named instead. " +
            "A handshake that does not finish, a name that does not match the host, or an untrusted certificate that someone else signed stays FAIL. " +
            "Copy puts the full report on the clipboard. Run again repeats the check.\r\n" +
            "\r\n" +
            "Restart as Administrator\r\n" +
            "The shield button is shown when this program is not elevated. " +
            "It asks you to save when there are unsaved changes, stops the listener, and starts a new elevated instance with the open file so that instance loads it and starts the server. " +
            "Cancelling the elevation prompt leaves this instance open and starts the listener again when it was running.\r\n" +
            "\r\n" +
            "Start and Stop\r\n" +
            "Start and the status sit on the right side of the toolbar. Help opens the Help tab in the panel under the log. " +
            "Start listens on the HTTP port (default http://127.0.0.1:5000/mcp) and, when the file enables it, HTTPS. " +
            "Stop closes the port. The button is disabled while the server is starting and until a valid configuration is loaded. " +
            "A bind address that is not loopback requires HTTPS and an API key. HTTP then stays on 127.0.0.1 and HTTPS listens on the network address. " +
            "When certificate.path is omitted, the server creates a self-signed certificate in %LOCALAPPDATA%\\mcp_overlord and reuses it. " +
            "Give server.cer to a remote client and have that client trust it before it connects. " +
            "The Signed certificate section explains how to use a certificate that clients already trust. " +
            "A public DNS name or IP belongs in certificate.hostNames, and the first name is the HTTPS URL when the bind address is 0.0.0.0 or ::.\r\n" +
            "\r\n" +
            "Signed certificate\r\n" +
            "A certificate signed by a certificate authority is trusted by a client without copying a file onto that client. " +
            "The authority checks that you control a DNS name, and that name is the host in the URL the client uses. " +
            "A public authority signs a name you can prove, such as play.example. " +
            "It does not sign 127.0.0.1, localhost, or a bare IP address. " +
            "Point the DNS name at this machine before you request the certificate. " +
            "Ask for a TLS server certificate. A client certificate or an email certificate will not serve HTTPS.\r\n" +
            "\r\n" +
            "Let's Encrypt with win-acme\r\n" +
            "win-acme is a Windows client for Let's Encrypt. Its program is wacs.exe, from https://www.win-acme.com/. " +
            "Let's Encrypt has to reach this machine from the internet, or reach the DNS host that answers for the name. " +
            "HTTP-01 validation opens port 80 on this machine and serves a short-lived file that Let's Encrypt fetches. " +
            "DNS-01 validation writes a TXT record at the DNS host instead, which works when port 80 is not reachable. " +
            "In wacs.exe, create a certificate for the DNS name and choose the store that writes a PFX file (PKCS#12). Set a password for that file. " +
            "A Let's Encrypt certificate lasts 90 days. win-acme installs a scheduled task that renews it at about 60 days. " +
            "This server reads the certificate when it starts, so restart the listener after each renewal. The previous certificate stays in use until then.\r\n" +
            "\r\n" +
            "A commercial or company certificate authority\r\n" +
            "Create a private key and a certificate signing request, then send only the request to the authority. " +
            "With OpenSSL the command is: openssl req -new -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj \"/CN=play.example\" -addext \"subjectAltName=DNS:play.example\". " +
            "Replace play.example with the DNS name clients will use. " +
            "Additional names go in the same subjectAltName list, separated by commas, for example DNS:play.example,DNS:www.play.example. " +
            "server.key is the private key and stays on this machine. server.csr is the request. " +
            "On Windows, certreq -new request.inf server.csr builds the same request. " +
            "The INF file needs a NewRequest section with Subject = \"CN=play.example\", KeySpec = 1, KeyLength = 2048, Exportable = TRUE, HashAlgorithm = SHA256, and RequestType = PKCS10, " +
            "plus an Extensions section with 2.5.29.17 = \"{text}\" and a following _continue_ = \"dns=play.example&\". " +
            "The authority returns the server certificate and, usually, one or more intermediate certificates. " +
            "Combine them into the PKCS#12 file this server loads: openssl pkcs12 -export -out server.pfx -inkey server.key -in server.crt -certfile chain.crt. " +
            "server.crt is the certificate for this machine. chain.crt is the intermediate file, or several intermediates concatenated. " +
            "The command asks for the PFX password. Keep that password for Settings.\r\n" +
            "\r\n" +
            "A private certificate authority\r\n" +
            "A root that you run can sign names a public authority will not, including names that exist only on a LAN. " +
            "Create the key and the request the same way as for a commercial authority, and have the private authority sign the request. " +
            "Import that root once into the Trusted Root Certification Authorities store on every client. " +
            "After that, those clients trust every server certificate the root signs. " +
            "A client that does not have the root still rejects the connection.\r\n" +
            "\r\n" +
            "Intermediate certificates\r\n" +
            "The listener presents the leaf certificate, the one whose subject is this server. " +
            "The client builds the chain from that leaf to a root it trusts. " +
            "Import the authority's intermediate certificates into the Intermediate Certification Authorities store on this machine so Windows can attach them to the handshake. " +
            "Putting the same intermediates in the PFX, with the -certfile chain.crt step above, ships them inside the file the listener loads. " +
            "The root belongs in the client's Trusted Root store. It does not belong in the PFX.\r\n" +
            "\r\n" +
            "Using the certificate\r\n" +
            "Open Settings. Check Save beside the certificate path and set it to the .pfx file. Check Save beside the password and enter the PFX password. " +
            "hostNames is copied into the certificate this server creates for itself. A file you supply already contains its names, so hostNames leaves that file unchanged. " +
            "Press OK, then Stop and Start. The listener loads the certificate at start. " +
            "Check connection should show TLS as PASS. " +
            "FAIL with a name that does not match the host means the host in the URL is absent from the certificate subject alternative names. " +
            "Issue a certificate that lists that host, or connect with a host the certificate already lists.\r\n" +
            "\r\n" +
            "Endpoints\r\n" +
            "Copy pastes the MCP URL. While the server is stopped the URLs are the ones the loaded file would use, and they are marked not live.\r\n" +
            "\r\n" +
            "Log\r\n" +
            "Each line has a local timestamp, a level (Info, Warning, Error), and a message. The filter shows one level or all of them. " +
            "Clear drops the visible buffer. The buffer keeps the configured log retention, 1,000 lines by default. Auto-scroll follows the tail and pauses if you scroll up. " +
            "Click a line to fill the Detail tab of the panel under the log. It shows the time, level, category, full message, source address and port, destination address and port, scheme, method, path, status, and duration. " +
            "Request and Response each have formatted JSON, a hex dump, and the UTF-8 text. A long base64 value in the JSON view is replaced with a note. The Image tab shows a returned screenshot and can save it. " +
            "The whole Detail tab, including the summary, Request, Response, and Image views, scrolls with one scrollbar. A JSON, hex, or text body taller than the pane keeps its own scrollbar. " +
            "A line that did not come from the network says so, and still shows a tool call when one was recorded. " +
            "API keys and certificate passwords are not written here.\r\n" +
            "\r\n" +
            "Stats\r\n" +
            "The Stats tab is in the panel under the log. " +
            "Uptime is the current run. Requests, errors, latency, screenshots, bytes, and inputs accumulate until the process exits. " +
            "Sessions is the number of MCP requests in flight.\r\n" +
            "\r\n" +
            "Tools\r\n" +
            "list_applications, get_application_description, launch_application, find_window, focus_window, take_screenshot, " +
            "mouse_move, mouse_click, mouse_down, mouse_up, mouse_scroll, key_press, key_down, key_up, type_text, is_locked, lock.\r\n" +
            "is_locked reports whether the workstation screen is locked. lock locks it. There is no unlock tool. " +
            "While the screen is locked, mouse and keyboard tools do nothing and say why. " +
            "A session should list applications, read a description when one exists, launch when the process is stopped, take a screenshot of the whole window, then click or type using pixels of that image. " +
            "coordinateSpace image (the default) uses those pixels, including scale and crop. coordinateSpace window_rect uses physical pixels of the window rectangle and ignores scale. " +
            "A point outside the window is rejected and no mouse input is sent. " +
            "The image includes the border and the title bar. A window that overlaps the target is not part of the image, and what is behind the window is not part of the image. A click on the outer border can resize the window. " +
            "The server focuses the target window before input. launch_application starts only the executable stored in the file. " +
            "A tool call that does not finish within 15 seconds returns an error, and the log records a warning. The work may still be running.\r\n" +
            "\r\n" +
            "clickBackend\r\n" +
            "mouseEvent (the default) uses SetCursorPos and mouse_event. sendInput uses SendInput. The choice is per application in the configuration file.\r\n" +
            "\r\n" +
            "Cursor on this machine\r\n" +
            "Add an mcp.json entry whose url is http://127.0.0.1:5000/mcp when the sample configuration is running. " +
            "No API key is required for loopback. With an API key, send Authorization: Bearer and the key. " +
            "This window does not open a tunnel or a firewall port. A remote client uses the HTTPS URL, trusts server.cer, and sends the API key. " +
            "Until that certificate is trusted, TLS does not protect the API key from interception.\r\n" +
            "\r\n" +
            "Start menu\r\n" +
            "After the installer has run, Open starts the program. " +
            "The installer adds Run as Administrator and Uninstall right away, without the program having to run first. " +
            "Run as Administrator starts an elevated instance. " +
            "The Start menu also lists recently used .overlord files. " +
            "Uninstall removes the program.\r\n" +
            "\r\n" +
            "Layout\r\n" +
            "The window opens again at the last position and size, including a window that was on another screen. " +
            "If that screen is off or disconnected, the window is moved onto a screen that is still on. " +
            "The window will not shrink so far that the toolbar buttons are cut off, and the configuration path shortens first. " +
            "The applications column width, the height of the panel under the log and its selected tab, the log detail tab, the JSON, hex, or ASCII view, " +
            "the log column widths, the log sort, the level filter, " +
            "and the application, server, and connection report positions are saved when the window closes.\r\n" +
            "\r\n" +
            "Command line\r\n" +
            "mcp_overlord file.overlord, or mcp_overlord --config <path>, loads that file and starts the server when the window opens. " +
            "Double-clicking a .overlord file does the same after the installer registers the extension. " +
            "--elevate starts an elevated instance and exits this one when that start succeeds. " +
            "Cancelling the prompt leaves this instance running. " +
            "--register-shell rebuilds the Start menu list and exits. " +
            "--help prints usage and exits.";
    }
}
