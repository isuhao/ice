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
    private static int run(string[] args, Ice.Communicator communicator)
    {
        Ice.ObjectAdapter adapter = communicator.createObjectAdapter("Throughput");
        Ice.Object obj = new ThroughputI();
        adapter.add(obj, Ice.Util.stringToIdentity("throughput"));
        adapter.activate();
        communicator.waitForShutdown();
        return 0;
    }
    
    public static void Main(string[] args)
    {
        int status = 0;
        Ice.Communicator communicator = null;
        
        try
        {
            Ice.Properties properties = Ice.Util.createProperties(ref args);
            properties.load("config");
            communicator = Ice.Util.initializeWithProperties(ref args, properties);
            status = run(args, communicator);
        }
        catch(System.Exception ex)
        {
	    System.Console.Error.WriteLine(ex);
            status = 1;
        }
        finally
        {
            if(communicator != null)
            {
                communicator.destroy();
            }
        }
        
        System.Environment.Exit(status);
    }
}
