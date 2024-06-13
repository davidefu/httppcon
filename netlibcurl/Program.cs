using System;
using System.Runtime.InteropServices;
using System.Text;

public class LibCurlWrapper
{
    const string LibCurlDll = "libcurl-x64.dll";
    private static string logfile;

    [DllImport(LibCurlDll)]
    public static extern CURLcode curl_global_init(long flags);

    [DllImport(LibCurlDll)]
    public static extern void curl_global_cleanup();

    [DllImport(LibCurlDll)]
    public static extern IntPtr curl_easy_init();

    [DllImport(LibCurlDll)]
    public static extern void curl_easy_cleanup(IntPtr handle);

    [DllImport(LibCurlDll)]
    public static extern CURLcode curl_easy_setopt(IntPtr handle, CURLoption option, IntPtr param);

    [DllImport(LibCurlDll)]
    public static extern CURLcode curl_easy_setopt(IntPtr handle, CURLoption option, Delegate param);

    [DllImport(LibCurlDll)]
    public static extern CURLcode curl_easy_perform(IntPtr handle);

    [DllImport(LibCurlDll)]
    public static extern CURLcode curl_easy_getinfo(IntPtr handle, CURLINFO info, out ulong value);

    public enum CURLcode : int
    {
        CURLE_OK = 0
    }

    public enum CURLoption : int
    {
        CURLOPT_URL = 10002,
        CURLOPT_VERBOSE = 41,
        CURLOPT_FORBID_REUSE = 75,
        CURLOPT_TIMEOUT = 13,
        CURLOPT_WRITEFUNCTION = 20011,
        CURLOPT_WRITEDATA = 10001,
        CURLOPT_HTTPHEADER = 10023,
        CURLOPT_DEBUGFUNCTION = 20094,
        CURLOPT_DEBUGDATA = 10095,
        CURLOPT_SSL_VERIFYPEER = 64
    }

    public enum CURLINFO : int
    {
        CURLINFO_RESPONSE_CODE = 0x200002,
        CURLINFO_CONN_ID = 0x600040,  // The ID for CURLINFO_CONN_ID,
        CURLINFO_XFER_ID = 0x60003F
    }

    // Delegate for the write callback
    public delegate UIntPtr WriteFunction(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata);

    // Write callback to capture response data
    public static UIntPtr WriteCallback(IntPtr ptr, UIntPtr size, UIntPtr nmemb, IntPtr userdata)
    {
        int realSize = (int)(size.ToUInt32() * nmemb.ToUInt32());
        byte[] buffer = new byte[realSize];
        Marshal.Copy(ptr, buffer, 0, realSize);

        GCHandle handle = GCHandle.FromIntPtr(userdata);
        StringBuilder sb = (StringBuilder)handle.Target;
        sb.Append(Encoding.UTF8.GetString(buffer));

        return (UIntPtr)realSize;
    }

    // Delegate for the debug callback
    public delegate int DebugFunction(IntPtr handle, CURLINFOTYPE type, IntPtr data, ulong size, IntPtr userptr);

    public enum CURLINFOTYPE
    {
        CURLINFO_TEXT = 0,
        CURLINFO_HEADER_IN,
        CURLINFO_HEADER_OUT,
        CURLINFO_DATA_IN,
        CURLINFO_DATA_OUT,
        CURLINFO_SSL_DATA_IN,
        CURLINFO_SSL_DATA_OUT,
        CURLINFO_END
    }

    // Debug callback function
    public static int DebugCallback(IntPtr handle, CURLINFOTYPE type, IntPtr data, ulong size, IntPtr userptr)
    {
        try
        {
            if (data != IntPtr.Zero)
            {
                string message = Marshal.PtrToStringAnsi(data, (int)size);
                WriteToLogFile($"Debug: {type} - {message}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            WriteToLogFile($"Exception in DebugCallback: {ex}");
            return 1; // Return non-zero to indicate error
        }
    }

    public static void PerformGetRequest(IntPtr curl, string url, string hostheader)
    {
        // Set URL
        curl_easy_setopt(curl, CURLoption.CURLOPT_URL, Marshal.StringToHGlobalAnsi(url));

        // Enable verbose mode for debugging
        curl_easy_setopt(curl, CURLoption.CURLOPT_VERBOSE, (IntPtr)1);

        // Enable connection reuse
        curl_easy_setopt(curl, CURLoption.CURLOPT_FORBID_REUSE, (IntPtr)0);

        // Set timeout
        curl_easy_setopt(curl, CURLoption.CURLOPT_TIMEOUT, (IntPtr)10);

        curl_easy_setopt(curl, CURLoption.CURLOPT_SSL_VERIFYPEER, (IntPtr)0);

        IntPtr headersList = IntPtr.Zero;
        if(!string.IsNullOrWhiteSpace(hostheader)) 
        {
            string h = $"Host: {hostheader}";
            headersList = curl_slist_append(headersList, h);
            curl_easy_setopt(curl, CURLoption.CURLOPT_HTTPHEADER, headersList);
        }

        // Set up string builder to capture the output
        StringBuilder responseData = new StringBuilder();
        GCHandle handle = GCHandle.Alloc(responseData);
        curl_easy_setopt(curl, CURLoption.CURLOPT_WRITEFUNCTION, Marshal.GetFunctionPointerForDelegate(new WriteFunction(WriteCallback)));
        curl_easy_setopt(curl, CURLoption.CURLOPT_WRITEDATA, GCHandle.ToIntPtr(handle));

        // Set debug callback
        DebugFunction debugCallback = new DebugFunction(DebugCallback);
        curl_easy_setopt(curl, CURLoption.CURLOPT_DEBUGFUNCTION, debugCallback);
        curl_easy_setopt(curl, CURLoption.CURLOPT_DEBUGDATA, IntPtr.Zero);

        // Perform the request
        CURLcode res = curl_easy_perform(curl);
        if (res != CURLcode.CURLE_OK)
        {
            WriteToLogFile("Error: " + res);
        }
        else
        {
            WriteToLogFile("Response Data: " + responseData.ToString());

            // Get the connection ID
            ulong connId;
            curl_easy_getinfo(curl, CURLINFO.CURLINFO_CONN_ID, out connId);
            WriteToLogFile("Connection ID: " + connId);

            ulong xfer;
            curl_easy_getinfo(curl, CURLINFO.CURLINFO_XFER_ID, out xfer);
            WriteToLogFile("Xfer ID: " + xfer);
        }

        // Cleanup
        handle.Free();
        curl_slist_free_all(headersList);
    }

    private static void WriteToLogFile(string v)
    {
        File.AppendAllText(logfile, $"{v}{Environment.NewLine}");
    }

    [DllImport(LibCurlDll)]
    private static extern IntPtr curl_slist_append(IntPtr list, string data);

    [DllImport(LibCurlDll)]
    private static extern void curl_slist_free_all(IntPtr list);

    static async Task Main(string[] args)
    {
        // Initialize the global environment
        curl_global_init(3);

        // Initialize a CURL easy handle
        IntPtr curl = curl_easy_init();
        if (curl != IntPtr.Zero)
        {
            // Read all lines from the CSV file
            var lines = File.ReadAllLines(args[0]);
            logfile = $"curloutput_{Path.GetFileNameWithoutExtension(args[0])}_{DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss")}.txt";
            // List to store the parsed data
            var data = new List<(string, int, string)>();

            foreach (var line in lines)
            {
                // Split each line by the comma delimiter
                var parts = line.Split(',');

                if (parts.Length >= 2)
                {
                    // Parse the first part as string and the second part as integer
                    string strValue = parts[0];
                    if (int.TryParse(parts[1], out int intValue))
                    {
                        var hostheader = "";
                        if (parts.Length > 2) 
                        {
                            hostheader = parts[2];
                        }
                        // Add the parsed data to the list
                        data.Add((strValue, intValue, hostheader));
                    }
                    else
                    {
                        WriteToLogFile($"Failed to parse integer from '{parts[1]}'");
                    }
                }
                else
                {
                    WriteToLogFile($"Invalid line format: {line}");
                }
            }

            // Iterate over the parsed values
            foreach (var (strValue, intValue, hostheader) in data)
            {
                WriteToLogFile($"{DateTime.Now.ToString("O")} Performing request to {strValue}");
                // Perform the HTTP GET request
                PerformGetRequest(curl, strValue, hostheader);
                WriteToLogFile($"{DateTime.Now.ToString("O")} Request completed to {strValue}");
                WriteToLogFile($"{DateTime.Now.ToString("O")} Wait for {intValue}");
                await Task.Delay(TimeSpan.FromSeconds(intValue));
            }

            // Clean up the CURL easy handle
            curl_easy_cleanup(curl);
        }

        // Clean up the global environment
        curl_global_cleanup();
    }
}
