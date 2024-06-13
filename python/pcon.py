import requests
import time
import sys
import os
from datetime import datetime
from urllib.parse import urlparse
from requests.adapters import HTTPAdapter
from requests.packages.urllib3.poolmanager import PoolManager

# Custom adapter to track TCP connections and log connection reuse information
class LoggingAdapter(HTTPAdapter):
    def __init__(self, *args, **kwargs):
        super(LoggingAdapter, self).__init__(*args, **kwargs)
        self.connection_info = {}

    def init_poolmanager(self, *args, **kwargs):
        self.poolmanager = PoolManager(*args, **kwargs)

    def send(self, request, *args, **kwargs):
        parsed_url = urlparse(request.url)

        try:

            response = super(LoggingAdapter, self).send(request, *args, **kwargs)

            fd = response.raw._original_response.fp.name

            connection_key = (parsed_url.scheme, parsed_url.hostname, parsed_url.port or (443 if parsed_url.scheme == 'https' else 80), fd)

            if connection_key in self.connection_info:
                reused = True
                self.connection_info[connection_key] += 1
            else:
                reused = False
                self.connection_info[connection_key] = 0

            status_code = response.status_code
            log_entry = f'Time: {datetime.now()} | URL: {request.url} | Status Code: {status_code} | Connection Reused: {reused} | FD: {response.raw._original_response.fp.name}'
        except Exception as e:
            response = None
            log_entry = f'Time: {datetime.now()} | URL: {request.url} | Exception: {e}'

        with open(output_file_name, 'a') as output_file:
            output_file.write(log_entry + '\n')
        print(log_entry)
        return response

    def print_summary(self):
        summary = "\nConnection Reuse Summary:\n"
        for connection_key, connections in self.connection_info.items():
            summary += f"Scheme: {connection_key[0]} | Hostname: {connection_key[1]} | Port: {connection_key[2]} | FD: {connection_key[3]} | Reused Connections: {connections}\n"
        with open(output_file_name, 'a') as output_file:
            output_file.write(summary)
        print(summary)

# Function to read URL, timeout, and optional host header values from the input file
def read_input_file(file_path):
    url_and_timeouts = []
    with open(file_path, 'r') as file:
        for line in file:
            parts = line.strip().split(',')
            url = parts[0]
            timeout = int(parts[1])
            host_header = parts[2] if len(parts) > 2 else None
            url_and_timeouts.append((url, timeout, host_header))
    return url_and_timeouts

# Get the input file name from command-line arguments
if len(sys.argv) != 2:
    print("Usage: python test_connection_reuse_requests.py <input_file>")
    sys.exit(1)

input_file_name = sys.argv[1]

# Generate the output file name
current_datetime = datetime.now().strftime('%Y-%m-%d_%H-%M-%S')
output_file_name = f'output_{os.path.splitext(os.path.basename(input_file_name))[0]}_{current_datetime}.txt'

# Read the input file
url_and_timeouts = read_input_file(input_file_name)

# Create a session to reuse connections
session = requests.Session()
adapter = LoggingAdapter()
session.mount('http://', adapter)
session.mount('https://', adapter)

# Clear the output file at the beginning
with open(output_file_name, 'w') as output_file:
    output_file.write('')

for i, (url, timeout, host_header) in enumerate(url_and_timeouts):
    headers = {}
    if host_header:
        headers['Host'] = host_header
    session.get(url, headers=headers)
    # Wait for the specified time before making the next request, except for the last one
    if i < len(url_and_timeouts) - 1:
        time.sleep(timeout)

session.close()

# Print the connection reuse summary
adapter.print_summary()