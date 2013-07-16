// **********************************************************************
//
// Copyright (c) 2003-2013 ZeroC, Inc. All rights reserved.
//
// This copy of Ice is licensed to you under the terms described in the
// ICE_LICENSE file included in this distribution.
//
// **********************************************************************

//
// This macro sets the default value only when compile with slice2cs.
//
#if defined(ICE_COMPILER) && (ICE_COMPILER == ICE_SLICE2CS)
#   define DEFAULT(X) = X
#else
#   define DEFAULT(X) /**/
#endif

//
// This macro sets the default value only when not compile with slice2cs.
//
#if defined(ICE_COMPILER) && (ICE_COMPILER != ICE_SLICE2CS)
#   define NODEFAULT(X) = X
#else
#   define NODEFAULT(X) /**/
#endif



module Test
{

class Default
{
    int x DEFAULT(10);
    int y DEFAULT(10);
};

class NoDefault
{
    int x NODEFAULT(10);
    int y NODEFAULT(10);
};

//
// This class is only defined when compile with slice2cs.
//
#if defined(ICE_COMPILER) && (ICE_COMPILER == ICE_SLICE2CS)
class CsOnly
{
    string lang DEFAULT("cs");
    int version DEFAULT(ICE_VERSION);
};
#endif

};