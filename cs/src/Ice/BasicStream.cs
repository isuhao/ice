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

namespace IceInternal
{

    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.Reflection;
    using System.Threading;

    public class BasicStream
    {
	public BasicStream(IceInternal.Instance instance)
	{
	    _instance = instance;
	    _bufferManager = instance.bufferManager();
	    _buf = _bufferManager.allocate(1500);
	    Debug.Assert(_buf != null);
	    _capacity = _buf.capacity();
	    _limit = 0;
	    Debug.Assert(_buf.limit() == _capacity);
	    
	    _readEncapsStack = null;
	    _writeEncapsStack = null;
	    _readEncapsCache = null;
	    _writeEncapsCache = null;
	    
	    _traceSlicing = -1;
	    
	    _marshalFacets = true;
	    _sliceObjects = true;
	    
	    _messageSizeMax = _instance.messageSizeMax(); // Cached for efficiency.

	    _objectList = null;
	}
	
	//
	// Do NOT use a finalizer, this would cause a sever performance
	// penalty! We must make sure that destroy() is called instead, to
	// reclaim resources.
	//
	public virtual void destroy()
	{
	    _bufferManager.reclaim(_buf);
	    _buf = null;
	}
	
	//
	// This function allows this object to be reused, rather than
	// reallocated.
	//
	public virtual void reset()
	{
	    _limit = 0;
	    _buf.limit(_capacity);
	    _buf.position(0);
	    
	    if(_readEncapsStack != null)
	    {
		Debug.Assert(_readEncapsStack.next == null);
		_readEncapsStack.next = _readEncapsCache;
		_readEncapsCache = _readEncapsStack;
		_readEncapsStack = null;
	    }

	    if(_objectList != null)
	    {
	        _objectList.Clear();
	    }
	}

	public virtual IceInternal.Instance instance()
	{
	    return _instance;
	}
	
	public virtual void swap(BasicStream other)
	{
	    Debug.Assert(_instance == other._instance);
	    
	    ByteBuffer tmpBuf = other._buf;
	    other._buf = _buf;
	    _buf = tmpBuf;
	    
	    int tmpCapacity = other._capacity;
	    other._capacity = _capacity;
	    _capacity = tmpCapacity;
	    
	    int tmpLimit = other._limit;
	    other._limit = _limit;
	    _limit = tmpLimit;
	    
	    ReadEncaps tmpRead = other._readEncapsStack;
	    other._readEncapsStack = _readEncapsStack;
	    _readEncapsStack = tmpRead;

	    tmpRead = other._readEncapsCache;
	    other._readEncapsCache = _readEncapsCache;
	    _readEncapsCache = tmpRead;

	    WriteEncaps tmpWrite = other._writeEncapsStack;
	    other._writeEncapsStack = _writeEncapsStack;
	    _writeEncapsStack = tmpWrite;

	    tmpWrite = other._writeEncapsCache;
	    other._writeEncapsCache = _writeEncapsCache;
	    _writeEncapsCache = tmpWrite;

	    int tmpReadSlice = other._readSlice;
	    other._readSlice = _readSlice;
	    _readSlice = tmpReadSlice;

	    int tmpWriteSlice = other._writeSlice;
	    other._writeSlice = _writeSlice;
	    _writeSlice = tmpWriteSlice;

	    ArrayList tmpObjectList = other._objectList;
	    other._objectList = _objectList;
	    _objectList = tmpObjectList;
	}
	
	public virtual void resize(int total, bool reading)
	{
	    if(total > _messageSizeMax)
	    {
		throw new Ice.MemoryLimitException("Message size > Ice.MessageSizeMax");
	    }
	    if(total > _capacity)
	    {
		int cap2 = _capacity << 1;
		int newCapacity = cap2 > total?cap2:total;
		_buf.limit(_limit);
		_buf.position(0);
		_buf = _bufferManager.reallocate(_buf, newCapacity);
		Debug.Assert(_buf != null);
		_capacity = _buf.capacity();
	    }
	    //
	    // If this stream is used for reading, then we want to set
	    // the buffer's limit to the new total size. If this buffer
	    // is used for writing, then we must set the buffer's limit
	    // to the buffer's capacity.
	    //
	    if(reading)
	    {
		_buf.limit(total);
	    }
	    else
	    {
		_buf.limit(_capacity);
	    }
	    _buf.position(total);
	    _limit = total;
	}
	
	public virtual ByteBuffer prepareRead()
	{
	    return _buf;
	}
	
	public virtual ByteBuffer prepareWrite()
	{
	    _buf.limit(_limit);
	    _buf.position(0);
	    return _buf;
	}
	
	public virtual void startWriteEncaps()
	{
	    {
		WriteEncaps curr = _writeEncapsCache;
		if(curr != null)
		{
		    if(curr.toBeMarshaledMap != null)
		    {
			curr.writeIndex = 0;
			curr.toBeMarshaledMap.Clear();
			curr.marshaledMap.Clear();
			curr.typeIdIndex = 0;
			curr.typeIdMap.Clear();
		    }
		    _writeEncapsCache = _writeEncapsCache.next;
		}
		else
		{
		    curr = new WriteEncaps();
		}
		curr.next = _writeEncapsStack;
		_writeEncapsStack = curr;
	    }
	    
	    _writeEncapsStack.start = _buf.position();
	    writeInt(0); // Placeholder for the encapsulation length.
	    writeByte(Protocol.encodingMajor);
	    writeByte(Protocol.encodingMinor);
	}
	
	public virtual void endWriteEncaps()
	{
	    Debug.Assert(_writeEncapsStack != null);
	    int start = _writeEncapsStack.start;
	    int sz = _buf.position() - start; // Size includes size and version.
	    _buf.putInt(start, sz);
	    
	    {
		WriteEncaps curr = _writeEncapsStack;
		_writeEncapsStack = curr.next;
		curr.next = _writeEncapsCache;
		_writeEncapsCache = curr;
	    }
	}
	
	public virtual void startReadEncaps()
	{
	    {
		ReadEncaps curr = _readEncapsCache;
		if(curr != null)
		{
		    if(curr.patchMap != null)
		    {
			curr.patchMap.Clear();
			curr.unmarshaledMap.Clear();
			curr.typeIdIndex = 0;
			curr.typeIdMap.Clear();
		    }
		    _readEncapsCache = _readEncapsCache.next;
		}
		else
		{
		    curr = new ReadEncaps();
		}
		curr.next = _readEncapsStack;
		_readEncapsStack = curr;
	    }
	    
	    _readEncapsStack.start = _buf.position();
	    
	    //
	    // I don't use readSize() and writeSize() for encapsulations,
	    // because when creating an encapsulation, I must know in
	    // advance how many bytes the size information will require in
	    // the data stream. If I use an Int, it is always 4 bytes. For
	    // readSize()/writeSize(), it could be 1 or 5 bytes.
	    //
	    int sz = readInt();
	    if(sz < 0)
	    {
		throw new Ice.NegativeSizeException();
	    }

	    if(sz - 4 > _buf.limit())
	    {
		throw new Ice.UnmarshalOutOfBoundsException();
	    }
	    _readEncapsStack.sz = sz;
	    
	    byte eMajor = readByte();
	    byte eMinor = readByte();
	    if(eMajor != Protocol.encodingMajor || eMinor > Protocol.encodingMinor)
	    {
		Ice.UnsupportedEncodingException e = new Ice.UnsupportedEncodingException();
		e.badMajor = eMajor < 0?eMajor + 256:eMajor;
		e.badMinor = eMinor < 0?eMinor + 256:eMinor;
		e.major = Protocol.encodingMajor;
		e.minor = Protocol.encodingMinor;
		throw e;
	    }
	    _readEncapsStack.encodingMajor = eMajor;
	    _readEncapsStack.encodingMinor = eMinor;
	}
	
	public virtual void endReadEncaps()
	{
	    Debug.Assert(_readEncapsStack != null);
	    int start = _readEncapsStack.start;
	    int sz = _readEncapsStack.sz;
	    try
	    {
		_buf.position(start + sz);
	    }
	    catch(ArgumentOutOfRangeException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	    
	    ReadEncaps curr = _readEncapsStack;
	    _readEncapsStack = curr.next;
	    curr.next = _readEncapsCache;
	    _readEncapsCache = curr;
	}
	
	public virtual void checkReadEncaps()
	{
	    Debug.Assert(_readEncapsStack != null);
	    int start = _readEncapsStack.start;
	    int sz = _readEncapsStack.sz;
	    if(_buf.position() != start + sz)
	    {
		throw new Ice.EncapsulationException();
	    }
	}
	
	public virtual int getReadEncapsSize()
	{
	    Debug.Assert(_readEncapsStack != null);
	    return _readEncapsStack.sz - 6;
	}
	
	public virtual void skipEncaps()
	{
	    int sz = readInt();
	    if(sz < 0)
	    {
		throw new Ice.NegativeSizeException();
	    }
	    try
	    {
		_buf.position(_buf.position() + sz - 4);
	    }
	    catch(ArgumentOutOfRangeException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void startWriteSlice()
	{
	    writeInt(0); // Placeholder for the slice length.
	    _writeSlice = _buf.position();
	}
	
	public virtual void endWriteSlice()
	{
	    int sz = _buf.position() - _writeSlice + 4;
	    _buf.putInt(_writeSlice - 4, sz);
	}
	
	public virtual void startReadSlice()
	{
	    int sz = readInt();
	    if(sz < 0)
	    {
		throw new Ice.NegativeSizeException();
	    }
	    _readSlice = _buf.position();
	}
	
	public virtual void endReadSlice()
	{
	}
	
	public virtual void skipSlice()
	{
	    int sz = readInt();
	    if(sz < 0)
	    {
		throw new Ice.NegativeSizeException();
	    }
	    try
	    {
		_buf.position(_buf.position() + sz - 4);
	    }
	    catch(ArgumentOutOfRangeException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeSize(int v)
	{
	    if(v > 254)
	    {
		expand(5);
		_buf.put((byte)255);
		_buf.putInt(v);
	    }
	    else
	    {
		expand(1);
		_buf.put((byte)v);
	    }
	}
	
	public virtual int readSize()
	{
	    try
	    {
		byte b = _buf.get();
		if(b == 255)
		{
		    int v = _buf.getInt();
		    if(v < 0)
		    {
			throw new Ice.NegativeSizeException();
		    }
		    return v;
		}
		else
		{
		    return (int) (b < 0?b + 256:b);
		}
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeTypeId(string id)
	{
	    object o = _writeEncapsStack.typeIdMap[id];
	    if(o != null)
	    {
		writeBool(true);
		writeSize((int)o);
	    }
	    else
	    {
		int index = ++_writeEncapsStack.typeIdIndex;
		_writeEncapsStack.typeIdMap[id] = index;
		writeBool(false);
		writeString(id);
	    }
	}
	
	public virtual string readTypeId()
	{
	    string id;
	    int index;
	    bool isIndex = readBool();
	    if(isIndex)
	    {
		index = readSize();
		id = (string)_readEncapsStack.typeIdMap[index];
		if(id == null)
		{
		    throw new Ice.UnmarshalOutOfBoundsException("Missing type ID");
		}
	    }
	    else
	    {
		id = readString();
		index = ++_readEncapsStack.typeIdIndex;
		_readEncapsStack.typeIdMap[index] = id;
	    }
	    return id;
	}
	
	public virtual void writeBlob(byte[] v)
	{
	    expand(v.Length);
	    _buf.put(v);
	}

	public virtual void writeBlob(Ice.ByteSeq v)
	{
	    expand(v.Count);
	    _buf.put(v.ToArray());
	}
	
	public virtual void readBlob(byte[] v)
	{
	    try
	    {
		_buf.get(v);
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}

	public virtual Ice.ByteSeq readBlob(int sz)
	{
	    byte[] v = new byte[sz];
	    try
	    {
		_buf.get(v);
		return new Ice.ByteSeq(v);
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeByte(byte v)
	{
	    expand(1);
	    _buf.put(v);
	}
	
	public virtual void writeByteSeq(byte[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length);
		_buf.put(v);
	    }
	}
	
	public virtual byte readByte()
	{
	    try
	    {
		return _buf.get();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual byte[] readByteSeq()
	{
	    try
	    {
		int sz = readSize();
		byte[] v = new byte[sz];
		_buf.get(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeBool(bool v)
	{
	    expand(1);
	    _buf.put(v ? (byte)1 : (byte)0);
	}
	
	public virtual void writeBoolSeq(bool[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length);
		_buf.putBoolSeq(v);
	    }
	}
	
	public virtual bool readBool()
	{
	    try
	    {
		return _buf.get() == 1;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual bool[] readBoolSeq()
	{
	    try
	    {
		int sz = readSize();
		bool[] v = new bool[sz];
		_buf.getBoolSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeShort(short v)
	{
	    expand(2);
	    _buf.putShort(v);
	}
	
	public virtual void writeShortSeq(short[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length * 2);
		_buf.putShortSeq(v);
	    }
	}
	
	public virtual short readShort()
	{
	    try
	    {
		return _buf.getShort();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual short[] readShortSeq()
	{
	    try
	    {
		int sz = readSize();
		short[] v = new short[sz];
		_buf.getShortSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeInt(int v)
	{
	    expand(4);
	    _buf.putInt(v);
	}
	
	public virtual void writeIntSeq(int[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length * 4);
		_buf.putIntSeq(v);
	    }
	}
	
	public virtual int readInt()
	{
	    try
	    {
		return _buf.getInt();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual int[] readIntSeq()
	{
	    try
	    {
		int sz = readSize();
		int[] v = new int[sz];
		_buf.getIntSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeLong(long v)
	{
	    expand(8);
	    _buf.putLong(v);
	}
	
	public virtual void writeLongSeq(long[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length * 8);
		_buf.putLongSeq(v);
	    }
	}
	
	public virtual long readLong()
	{
	    try
	    {
		return _buf.getLong();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual long[] readLongSeq()
	{
	    try
	    {
		int sz = readSize();
		long[] v = new long[sz];
		_buf.getLongSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeFloat(float v)
	{
	    expand(4);
	    _buf.putFloat(v);
	}
	
	public virtual void writeFloatSeq(float[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length * 4);
		_buf.putFloatSeq(v);
	    }
	}
	
	public virtual float readFloat()
	{
	    try
	    {
		return _buf.getFloat();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual float[] readFloatSeq()
	{
	    try
	    {
		int sz = readSize();
		float[] v = new float[sz];
		_buf.getFloatSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual void writeDouble(double v)
	{
	    expand(8);
	    _buf.putDouble(v);
	}
	
	public virtual void writeDoubleSeq(double[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		expand(v.Length * 8);
		_buf.putDoubleSeq(v);
	    }
	}
	
	public virtual double readDouble()
	{
	    try
	    {
		return _buf.getDouble();
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}
	
	public virtual double[] readDoubleSeq()
	{
	    try
	    {
		int sz = readSize();
		double[] v = new double[sz];
		_buf.getDoubleSeq(v);
		return v;
	    }
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	}

	private static System.Text.UTF8Encoding utf8 = new System.Text.UTF8Encoding(false, true);
       
	public virtual void writeString(string v)
	{
	    if(v == null || v.Length == 0)
	    {
		writeSize(0);
		return;
	    }
	    try
	    {
		byte[] arr = utf8.GetBytes(v);
		writeSize(arr.Length);
		expand(arr.Length);
		_buf.put(arr);
	    }
	    catch(Exception)
	    {
		Debug.Assert(false);
	    }
	}
	
	public virtual void writeStringSeq(string[] v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Length);
		for(int i = 0; i < v.Length; i++)
		{
		    writeString(v[i]);
		}
	    }
	}

	public virtual void writeStringSeq(Ice.StringSeq v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Count);
		for(int i = 0; i < v.Count; i++)
		{
		    writeString(v[i]);
		}
	    }
	}
	
	public virtual void writeStringSeq(Ice.FacetPath v)
	{
	    if(v == null)
	    {
		writeSize(0);
	    }
	    else
	    {
		writeSize(v.Count);
		for(int i = 0; i < v.Count; i++)
		{
		    writeString(v[i]);
		}
	    }
	}

	public virtual string readString()
	{
	    int len = readSize();
	    
	    if(len == 0)
	    {
		return "";
	    }
	    
	    try
	    {
		//
		// We reuse the _stringBytes array to avoid creating
		// excessive garbage
		//
		if(_stringBytes == null || len > _stringBytes.Length)
		{
		    _stringBytes = new byte[len];
		}
		_buf.get(_stringBytes, 0, len);
		return utf8.GetString(_stringBytes, 0, len);
	    }    
	    catch(InvalidOperationException ex)
	    {
		throw new Ice.UnmarshalOutOfBoundsException(ex);
	    }
	    catch(Exception)
	    {
		Debug.Assert(false);
		return "";
	    }
	}
	
	public virtual Ice.StringSeq readStringSeq()
	{
	    int sz = readSize();
	    Ice.StringSeq v = new Ice.StringSeq();
	    for(int i = 0; i < sz; i++)
	    {
		v.Add(readString());
	    }
	    return v;
	}

	public virtual Ice.FacetPath readFacetPath()
	{
	    int sz = readSize();
	    Ice.FacetPath v = new Ice.FacetPath();
	    for(int i = 0; i < sz; i++)
	    {
		v.Add(readString());
	    }
	    return v;
	}
	
	public virtual void writeProxy(Ice.ObjectPrx v)
	{
	    _instance.proxyFactory().proxyToStream(v, this);
	}
	
	public virtual Ice.ObjectPrx readProxy()
	{
	    return _instance.proxyFactory().streamToProxy(this);
	}
	
	public virtual void writeObject(Ice.Object v)
	{
	    if(_writeEncapsStack == null)
	    // Lazy initialization
	    {
		_writeEncapsStack = _writeEncapsCache;
		if(_writeEncapsStack != null)
		{
		    _writeEncapsCache = _writeEncapsCache.next;
		}
		else
		{
		    _writeEncapsStack = new WriteEncaps();
		}
	    }
	    
	    if(_writeEncapsStack.toBeMarshaledMap == null) // Lazy initialization
	    {
		_writeEncapsStack.toBeMarshaledMap = new Hashtable();
		_writeEncapsStack.marshaledMap = new Hashtable();
		_writeEncapsStack.typeIdMap = new Hashtable();
	    }
	    if(v != null)
	    {
		//
		// Look for this instance in the to-be-marshaled map.
		//
		object p = _writeEncapsStack.toBeMarshaledMap[v];
		if(p == null)
		{
		    //
		    // Didn't find it, try the marshaled map next.
		    //
		    object q = _writeEncapsStack.marshaledMap[v];
		    if(q == null)
		    {
			//
			// We haven't seen this instance previously, create a new index, and
			// insert it into the to-be-marshaled map.
			//
			q = ++_writeEncapsStack.writeIndex;
			_writeEncapsStack.toBeMarshaledMap[v] = q;
		    }
		    p = q;
		}
		writeInt(-((int)p));
	    }
	    else
	    {
		writeInt(0); // Write null reference
	    }
	}
	
	public virtual void readObject(IceInternal.Patcher patcher)
	{
	    Ice.Object v = null;
	    
	    if(_readEncapsStack == null)
	    // Lazy initialization
	    {
		_readEncapsStack = _readEncapsCache;
		if(_readEncapsStack != null)
		{
		    _readEncapsCache = _readEncapsCache.next;
		}
		else
		{
		    _readEncapsStack = new ReadEncaps();
		}
	    }
	    
	    if(_readEncapsStack.patchMap == null) // Lazy initialization
	    {
		_readEncapsStack.patchMap = new Hashtable();
		_readEncapsStack.unmarshaledMap = new Hashtable();
		_readEncapsStack.typeIdMap = new Hashtable();
	    }
	    
	    int index = readInt();
	    
	    if(index == 0)
	    {
		patcher.patch(null);
		return;
	    }
	    
	    if(index < 0 && patcher != null)
	    {
		int i = -index;
		IceUtil.LinkedList patchlist = (IceUtil.LinkedList)_readEncapsStack.patchMap[i];
		if(patchlist == null)
		{
		    //
		    // We have no outstanding instances to be patched for this index, so make a new entry
		    // in the patch map.
		    //
		    patchlist = new IceUtil.LinkedList();
		    _readEncapsStack.patchMap[i] = patchlist;
		}
		//
		// Append a patcher for this instance and see if we can patch the instance. (The instance
		// may have been unmarshaled previously.)
		//
		patchlist.Add(patcher);
		patchReferences(null, i);
		return;
	    }
	    
	    while(true)
	    {
		string id = readTypeId();
		
		//
		// Try to find a factory registered for the specific type.
		//
		Ice.ObjectFactory userFactory = _instance.servantFactoryManager().find(id);
		if(userFactory != null)
		{
		    v = userFactory.create(id);
		}
		
		//
		// If that fails, invoke the default factory if one has been registered.
		//
		if(v == null)
		{
		    userFactory = _instance.servantFactoryManager().find("");
		    if(userFactory != null)
		    {
			v = userFactory.create(id);
		    }
		}
		
		//
		// There isn't a static factory for Ice::Object, so check for that case now.
		// We do this *after* the factory inquiries above so that a factory could be
		// registered for "::Ice::Object".
		//
		if(v == null && id.Equals(Ice.ObjectImpl.ice_staticId()))
		{
		    v = new Ice.ObjectImpl();
		}
		
		//
		// Last chance: check the table of static factories (i.e., automatically generated
		// factories for concrete classes).
		//
		if(v == null)
		{
		    userFactory = loadObjectFactory(id);
		    if(userFactory != null)
		    {
			v = userFactory.create(id);
		    }
		}
		
		if(v == null)
		{
		    if(_sliceObjects)
		    {
			//
			// Performance sensitive, so we use lazy initialization for tracing.
			//
			if(_traceSlicing == -1)
			{
			    _traceSlicing = _instance.traceLevels().slicing;
			    _slicingCat = _instance.traceLevels().slicingCat;
			}
			if(_traceSlicing > 0)
			{
			    TraceUtil.traceSlicing("class", id, _slicingCat, _instance.logger());
			}
			skipSlice(); // Slice off this derived part -- we don't understand it.
			continue;
		    }
		    else
		    {
		        Ice.NoObjectFactoryException ex = new Ice.NoObjectFactoryException();
			ex.type = id;
			throw ex;
		    }
		}
		
		int i = index;
		_readEncapsStack.unmarshaledMap[i] = v;

		//
		// Record each object instance so that readPendingObjects can invoke ice_postUnmarshal
		// after all objects have been unmarshaled.
		//
		if(_objectList == null)
		{
		    _objectList = new ArrayList();
		}
		_objectList.Add(v);

		v.__read(this, false);
		patchReferences(i, null);
		return;
	    }
	}
	
	public virtual void writeUserException(Ice.UserException v)
	{
	    writeBool(v.__usesClasses());
	    v.__write(this);
	    if(v.__usesClasses())
	    {
		writePendingObjects();
	    }
	}
	
	public virtual void throwException()
	{
	    bool usesClasses = readBool();
	    
	    string id = readString();
	    
	    while(!id.Equals(""))
	    {
		//
		// Look for a factory for this ID.
		//
		UserExceptionFactory factory = _instance.userExceptionFactoryManager().find(id);
		if(factory == null)
		{
		    factory = loadUserExceptionFactory(id);
		}
		
		if(factory != null)
		{
		    //
		    // Got factory -- get the factory to instantiate the exception, initialize the
		    // exception members, and throw the exception.
		    //
		    try
		    {
			factory.createAndThrow();
		    }
		    catch(Ice.UserException ex)
		    {
			ex.__read(this, false);
			if(usesClasses)
			{
			    readPendingObjects();
			}
			throw ex;
		    }
		}
		else
		{
		    //
		    // Performance sensitive, so we use lazy initialization for tracing.
		    //
		    if(_traceSlicing == -1)
		    {
			_traceSlicing = _instance.traceLevels().slicing;
			_slicingCat = _instance.traceLevels().slicingCat;
		    }
		    if(_traceSlicing > 0)
		    {
			TraceUtil.traceSlicing("exception", id, _slicingCat, _instance.logger());
		    }
		    skipSlice(); // Slice off what we don't understand.
		    id = readString(); // Read type id for next slice.
		}
	    }

	    //
	    // We can get here only if the sender has marshaled a sequence
	    // of type Ids, none of which we have factory for. This means
	    // that sender and receiver disagree about the Slice
	    //  definitions they use.
	    //
	    throw new Ice.UnknownUserException("Client and server disagree about the type IDs in use");
	}
	
	public virtual void writePendingObjects()
	{
	    if(_writeEncapsStack != null && _writeEncapsStack.toBeMarshaledMap != null)
	    {
		while(_writeEncapsStack.toBeMarshaledMap.Count > 0)
		{
		    Hashtable savedMap = new Hashtable(_writeEncapsStack.toBeMarshaledMap);
		    writeSize(savedMap.Count);
		    foreach(DictionaryEntry e in savedMap)
		    {
			//
			// Add an instance from the old to-be-marshaled map to the marshaled map and then
			// ask the instance to marshal itself. Any new class instances that are triggered
			// by the classes marshaled are added to toBeMarshaledMap.
			//
			_writeEncapsStack.marshaledMap[e.Key] = e.Value;
			writeInstance((Ice.Object)e.Key, (int)e.Value);
		    }
		    
		    //
		    // We have marshaled all the instances for this pass, substract what we have
		    // marshaled from the toBeMarshaledMap.
		    //
		    foreach(DictionaryEntry e in savedMap)
		    {
			_writeEncapsStack.toBeMarshaledMap.Remove(e.Key);
		    }
		}
	    }
	    writeSize(0); // Zero marker indicates end of sequence of sequences of instances.
	}
	
	public virtual void readPendingObjects()
	{
	    int num;
	    do 
	    {
		num = readSize();
		for(int k = num; k > 0; --k)
		{
		    readObject(null);
		}
	    }
	    while(num > 0);

	    //
	    // Iterate over unmarshaledMap and invoke ice_postUnmarshal on each object.
	    // We must do this after all objects in this encapsulation have been
	    // unmarshaled in order to ensure that any object data members have been
	    // properly patched.
	    //
	    if(_objectList != null)
	    {
		foreach(Ice.Object obj in _objectList)
		{
		    try
		    {
		        obj.ice_postUnmarshal();
		    }
		    catch(System.Exception ex)
		    {
		        _instance.logger().warning("exception raised by ice_postUnmarshal::\n" + ex);
		    }
		}
	    }
	}
	
	public virtual void marshalFacets(bool b)
	{
	    _marshalFacets = b;
	}

	public void
	sliceObjects(bool b)
	{
	    _sliceObjects = b;
	}
	
	internal virtual void writeInstance(Ice.Object v, int index)
	{
	    writeInt(index);
	    try
	    {
	        v.ice_preMarshal();
	    }
	    catch(System.Exception ex)
	    {
		_instance.logger().warning("exception raised by ice_preMarshal::\n" + ex);
	    }
	    v.__write(this, _marshalFacets);
	}
	
	internal virtual void patchReferences(object instanceIndex, object patchIndex)
	{
	    //
	    // Called whenever we have unmarshaled a new instance or an index.
	    // The instanceIndex is the index of the instance just unmarshaled and patchIndex is the index
	    // just unmarshaled. (Exactly one of the two parameters must be null.)
	    // Patch any pointers in the patch map with the new address.
	    //
	    Debug.Assert(   ((object)instanceIndex != null && (object)patchIndex == null)
	                 || ((object)instanceIndex == null && (object)patchIndex != null));
	    
	    IceUtil.LinkedList patchlist;
	    Ice.Object v;
	    if((object)instanceIndex != null)
	    {
		//
		// We have just unmarshaled an instance -- check if something needs patching for that instance.
		//
		patchlist = (IceUtil.LinkedList)_readEncapsStack.patchMap[instanceIndex];
		if(patchlist == null)
		{
		    return; // We don't have anything to patch for the instance just unmarshaled
		}
		v = (Ice.Object)_readEncapsStack.unmarshaledMap[instanceIndex];
		patchIndex = instanceIndex;
	    }
	    else
	    {
		//
		// We have just unmarshaled an index -- check if we have unmarshaled the instance for that index yet.
		//
		v = (Ice.Object)_readEncapsStack.unmarshaledMap[patchIndex];
		if(v == null)
		{
		    return; // We haven't unmarshaled the instance for this index yet
		}
		patchlist = (IceUtil.LinkedList)_readEncapsStack.patchMap[patchIndex];
	    }
	    Debug.Assert(patchlist != null && patchlist.Count > 0);
	    Debug.Assert(v != null);
	    
	    //
	    // Patch all references that refer to the instance.
	    //
	    foreach(IceInternal.Patcher patcher in patchlist)
	    {
		try
		{
		    patcher.patch(v);
		}
		catch(InvalidCastException ex)
		{
		    Ice.NoObjectFactoryException nof = new Ice.NoObjectFactoryException(ex);
		    nof.type = patcher.type();
		    throw nof;
		}
	    }
	    
	    //
	    // Clear out the patch map for that index -- there is nothing left to patch for that
	    // index for the time being.
	    //
	    _readEncapsStack.patchMap.Remove(patchIndex);
	}
	
	internal virtual int pos()
	{
	    return _buf.position();
	}
	
	internal virtual void pos(int n)
	{
	    _buf.position(n);
	}
	
	internal virtual int size()
	{
	    return _limit;
	}

	virtual internal bool isEmpty()
	{
	    return _limit == 0;
	}
	
	private void expand(int size)
	{
	    if(_buf.position() == _limit)
	    {
		int oldLimit = _limit;
		_limit += size;
		if(_limit > _messageSizeMax)
		{
		    throw new Ice.MemoryLimitException("Message larger than Ice.MessageSizeMax");
		}
		if(_limit > _capacity)
		{
		    int cap2 = _capacity << 1;
		    int newCapacity = cap2 > _limit ? cap2 : _limit;
		    _buf.limit(oldLimit);
		    int pos = _buf.position();
		    _buf.position(0);
		    _buf = _bufferManager.reallocate(_buf, newCapacity);
		    Debug.Assert(_buf != null);
		    _capacity = _buf.capacity();
		    _buf.limit(_capacity);
		    _buf.position(pos);
		}
	    }
	}
	
	private sealed class DynamicObjectFactory : Ice.LocalObjectImpl, Ice.ObjectFactory
	{
	    internal DynamicObjectFactory(Type c)
	    {
		_class = c;
	    }
	    
	    public Ice.Object create(string type)
	    {
		try
		{
		    return (Ice.Object)SupportClass.CreateNewInstance(_class);
		}
		catch(Exception ex)
		{
		    throw new Ice.SyscallException(ex);
		}
	    }
	    
	    public void destroy()
	    {
	    }
	    
	    private Type _class;
	}
     
	//
	// Make sure that all assemblies that are referenced by this process
	// are actually loaded. This is necessary so we can use reflection
	// on any type in any assembly (because the type we are after will
	// most likely not be in the current assembly and, worse, may be
	// in an assembly that has not been loaded yet. (Type.GetType()
	// is no good because it looks only in the calling object's assembly
	// and mscorlib.dll.)
	//
	private static void loadAssemblies()
	{
	    if(!_assembliesLoaded) // Lazy initialization
	    {
		_assemblyMutex.WaitOne();
		try 
		{
		    if(!_assembliesLoaded) // Double-checked locking
		    {
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach(Assembly a in assemblies)
			{
			    _loadedAssemblies[a.FullName] = a;
			}
			foreach(Assembly a in assemblies)
			{
			    loadReferencedAssemblies(a);
			}
			_assembliesLoaded = true;
		    }
		}
		catch(Exception)
		{
		    Debug.Assert(false);
		}
		finally
		{
		    _assemblyMutex.ReleaseMutex();
		}
	    }
	}

	private static void loadReferencedAssemblies(Assembly a)
	{
	    AssemblyName[] names = a.GetReferencedAssemblies();
	    foreach(AssemblyName name in names)
	    {
		if(!_loadedAssemblies.Contains(name.FullName))
		{
		    Assembly ra = Assembly.Load(name);
		    _loadedAssemblies[ra.FullName] = ra;
		    loadReferencedAssemblies(ra);
		}
	    }
	}

	private static Type findTypeForId(string id)
	{
	    _assemblyMutex.WaitOne();
	    try {
		string csharpId = typeToClass(id);
		Type t = (Type)_typeTable[id];
		if(t != null)
		{
		    return t;
		}
		foreach(Assembly a in _loadedAssemblies.Values)
		{
		    if((t = a.GetType(csharpId)) != null)
		    {
			_typeTable[csharpId] = t;
			return t;
		    }
		}
	    }
	    finally
	    {
		_assemblyMutex.ReleaseMutex();
	    }
	    return null;
	}

	private Ice.ObjectFactory loadObjectFactory(string id)
	{
	    Ice.ObjectFactory factory = null;
	    
	    //UPGRADE_NOTE: Exception 'java.lang.ClassNotFoundException' was converted to 'Exception' which has different behavior. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1100"'
	    try
	    {
		loadAssemblies(); // Lazy initialization
		
		Type c = findTypeForId(id);
		if(c == null)
		{
		    return null;
		}
		//
		// Ensure the class is instantiable.
		//
		if(!c.IsAbstract && !c.IsInterface)
		{
		    Ice.ObjectFactory dynamicFactory = new DynamicObjectFactory(c);
		    //
		    // We will try to install the dynamic factory, but another thread
		    // may install a factory first.
		    //
		    while(factory == null)
		    {
			try
			{
			    _instance.servantFactoryManager().add(dynamicFactory, id);
			    factory = dynamicFactory;
			}
			catch(Ice.AlreadyRegisteredException)
			{
			    //
			    // Another thread already installed the factory, so try
			    // to obtain it. It's possible (but unlikely) that the factory
			    // will have already been removed, in which case the return
			    // value will be null and the while loop will attempt to
			    // install the dynamic factory again.
			    //
			    factory = _instance.servantFactoryManager().find(id);
			}
		    }
		}
	    }
	    catch(Exception ex)
	    {
		    Ice.NoObjectFactoryException e = new Ice.NoObjectFactoryException(ex);
		    e.type = id;
		    throw e;
	    }
	    
	    return factory;
	}
	
	private sealed class DynamicUserExceptionFactory : Ice.LocalObjectImpl, UserExceptionFactory
	{
	    internal DynamicUserExceptionFactory(Type c)
	    {
		_class = c;
	    }
	    
	    public void createAndThrow()
	    {
		try
		{
		    throw (Ice.UserException)SupportClass.CreateNewInstance(_class);
		}
		catch(Ice.UserException ex)
		{
		    throw ex;
		}
		catch(Exception ex)
		{
		    throw new Ice.SyscallException(ex);
		}
	    }
	    
	    public void destroy()
	    {
	    }
	    
	    private Type _class;
	}
	
	private UserExceptionFactory loadUserExceptionFactory(string id)
	{
	    UserExceptionFactory factory = null;

	    try
	    {
		loadAssemblies(); // Lazy initialization

		Type c = findTypeForId(id);
		if(c == null)
		{
		    return null;
		}
		//
		// Ensure the class is instantiable.
		//
		Debug.Assert(!c.IsAbstract && !c.IsInterface);
		factory = new DynamicUserExceptionFactory(c);
		_instance.userExceptionFactoryManager().add(factory, id);
	    }
	    catch(Exception ex)
	    {
		throw new Ice.UnknownUserException(ex);
	    }
	    
	    return factory;
	}
	
	private static string typeToClass(string id)
	{
	    if(!id.StartsWith("::"))
	    {
		throw new Ice.MarshalException("type ID does not start with `::'");
	    }
	    return id.Substring(2).Replace("::", ".");
	}
	
	private IceInternal.Instance _instance;
	private BufferManager _bufferManager;
	private ByteBuffer _buf;
	private int _capacity; // Cache capacity to avoid excessive method calls
	private int _limit; // Cache limit to avoid excessive method calls
	private byte[] _stringBytes; // Reusable array for reading strings
	
	private sealed class ReadEncaps
	{
	    internal int start;
	    internal int sz;
	    
	    internal byte encodingMajor;
	    internal byte encodingMinor;
	    
	    internal Hashtable patchMap;
	    internal Hashtable unmarshaledMap;
	    internal int typeIdIndex;
	    internal Hashtable typeIdMap;
	    internal ReadEncaps next;
	}
	
	private sealed class WriteEncaps
	{
	    internal int start;
	    
	    internal int writeIndex;
	    internal Hashtable toBeMarshaledMap;
	    internal Hashtable marshaledMap;
	    internal int typeIdIndex;
	    internal Hashtable typeIdMap;
	    internal WriteEncaps next;
	}
	
	private ReadEncaps _readEncapsStack;
	private WriteEncaps _writeEncapsStack;
	private ReadEncaps _readEncapsCache;
	private WriteEncaps _writeEncapsCache;
	
	private int _readSlice;
	private int _writeSlice;
	
	private int _traceSlicing;
	private string _slicingCat;
	
	private bool _marshalFacets;
	private bool _sliceObjects;
	
	private int _messageSizeMax;

	private ArrayList _objectList;

	private static volatile bool _assembliesLoaded = false;
	private static Hashtable _loadedAssemblies = new Hashtable(); // <string, Assembly> pairs
	private static Hashtable _typeTable = new Hashtable(); // <type name, Type> pairs
	private static Mutex _assemblyMutex = new Mutex(); // Protects the above three members
    }

}
