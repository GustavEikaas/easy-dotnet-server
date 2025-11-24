# Easy Dotnet Server

## Description

**Easy Dotnet Server** is the lightweight C# JSON-RPC server powering the [easy-dotnet.nvim](https://github.com/GustavEikaas/easy-dotnet.nvim) Neovim plugin.

The server communicates via named pipes using JSON-RPC and provides a unified response format for the plugin.

## Features

* JSON-RPC 2.0 communication over named pipes
* Asynchronous, multi-client server support
* MsBuild integration
* Nuget integration
* MTP integration
* VsTest integration

## Use Case

This server is an internal component of the `easy-dotnet.nvim` plugin and is **not intended for standalone use**.

## 📚 RPC API Documentation

All RPC methods exposed by the server are documented in the auto-generated [`rpcDoc.md`](./rpcDoc.md) file.

This file includes:
- JSON-RPC method names
- Parameter names, types, and optionality
- Return types
- The associated controller for each method

You can regenerate this file at any time by running the server with the `generate-rpc-docs` command:

```bash
dotnet run generate-rpc-docs -- --format markdown
