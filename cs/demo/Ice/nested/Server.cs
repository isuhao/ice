// **********************************************************************
//
// Copyright (c) 2003
// ZeroC, Inc.
// Billerica, MA, USA
//
// All Rights Reserved.
//
// Ice is free software; you can redistribute it and/or modify it under
// the terms of the GNU General Public License version 2 as published by
// the Free Software Foundation.
//
// **********************************************************************

public class Server
{
    public static void Main(string[] args)
    {
        NestedServer app = new NestedServer();
        int status = app.main(args, "config.server");
        System.Environment.Exit(status);
    }
}
