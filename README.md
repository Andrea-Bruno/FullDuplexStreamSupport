# PipeStream: Full-Duplex Communication Stream for Data Streaming Systems

## Overview

`PipeStream` is a powerful and flexible library designed to facilitate full-duplex communication using various data streaming systems, including but not limited to named pipes, in .NET applications. This library provides both synchronous and asynchronous methods for reading and writing data, managing client connections, and handling data transmission with specified timeouts. It is ideal for scenarios where efficient inter-process communication (IPC) is required.

## Features

- **Full-Duplex Communication**: Supports simultaneous reading and writing operations.
- **Asynchronous and Synchronous Methods**: Provides both async and sync methods for data operations.
- **Timeout Handling**: Allows specifying timeouts for connection attempts.
- **Client Management**: Manages multiple client connections efficiently.
- **Thread-Safe Operations**: Ensures thread safety for concurrent data access.
- **Single Channel Communication**: Enables effective communication between multiple clouds using a single input and output channel without collision issues.

## Installation

'''bash
To install `PipeStream`, you can use the NuGet Package Manager:
'''

dotnet add package FullDuplexStreamSupport


## Usage

### Initializing the PipeStream

To initialize the `PipeStream` with input and output streams:

```csharp
Stream pipeIn = ...; // Your input stream Stream pipeOut = ...; // Your output stream PipeStream.Initialize(pipeIn, pipeOut, OnNewClientConnected);
```


### Connecting with Timeout

To connect to the pipe stream with a specified timeout:

```csharp
var pipeStream = new PipeStream(clientId); pipeStream.Connect(timeoutMs: 5000); // Timeout in millisecond
```

### Reading and Writing Data

To write data to the pipe stream:

```csharp
byte[] dataToSend = ...; await pipeStream.WriteAsync(dataToSend, 0, dataToSend.Length);
```

To read data from the pipe stream:

```csharp
byte[] buffer = new byte[1024]; int bytesRead = await pipeStream.ReadAsync(buffer, 0, buffer.Length);
```


## Scenarios of Use

### Inter-Process Communication (IPC)

`PipeStream` is ideal for IPC scenarios where different processes need to communicate efficiently. It can be used in client-server architectures, where the server handles multiple client connections simultaneously.

### Real-Time Data Streaming

For applications requiring real-time data streaming, such as live video or audio streaming, `PipeStream` provides the necessary full-duplex communication capabilities to handle continuous data flow.

### Remote Procedure Calls (RPC)

In distributed systems, `PipeStream` can be used to implement RPC mechanisms, allowing remote execution of procedures and functions across different processes or machines.

### Blazor and ASP.NET Core Integration

`PipeStream` can be integrated into Blazor and ASP.NET Core applications to enable real-time communication between the server and clients. This is particularly useful for applications requiring live updates, such as chat applications, online gaming, and collaborative tools.

### Single Channel Communication

`PipeStream` enables effective communication between multiple clouds using a single input and output channel. This ensures that input and output operations do not collide, allowing for seamless data transmission without interference.

## Contributing

We welcome contributions to `PipeStream`. If you have any ideas, suggestions, or bug reports, please open an issue or submit a pull request on our GitHub repository.

## License

`PipeStream` is licensed under the MIT License. See the [LICENSE](LICENSE) file for more details.

## Contact

For any questions or inquiries, please contact us.

---

By using `PipeStream`, you can leverage the power of various data streaming systems for efficient and reliable full-duplex communication in your .NET applications. Whether you are building IPC mechanisms, real-time data streaming solutions, or integrating with Blazor and ASP.NET Core, `PipeStream` provides the tools you need to succeed.

---





