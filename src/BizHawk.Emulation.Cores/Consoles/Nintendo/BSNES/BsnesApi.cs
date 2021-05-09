﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BizHawk.Common;
using BizHawk.Emulation.Cores.Waterbox;
using BizHawk.BizInvoke;
using BizHawk.Emulation.Common;

namespace BizHawk.Emulation.Cores.Nintendo.SNES
{
	public abstract unsafe class BsnesCoreImpl
	{
		[BizImport(CallingConvention.Cdecl, Compatibility = true)]
		public abstract IntPtr DllInit();

		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_set_audio_enabled(bool enabled);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_set_video_enabled(bool enabled);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_set_layer_enables(BsnesApi.LayerEnables layerEnables);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_set_trace_enabled(bool enabled);

		[BizImport(CallingConvention.Cdecl)]
		public abstract BsnesApi.SNES_REGION snes_get_region();
		[BizImport(CallingConvention.Cdecl)]
		public abstract BsnesApi.SNES_MAPPER snes_get_mapper();
		[BizImport(CallingConvention.Cdecl)]
		public abstract void* snes_get_memory_region(int id, out int size, out int wordSize);
		[BizImport(CallingConvention.Cdecl)]
		public abstract byte snes_bus_read(uint address);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_bus_write(uint address, byte value);

		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_set_callbacks(
			BsnesApi.snes_input_poll_t inputPollCb, BsnesApi.snes_input_state_t inputStateCb,
			BsnesApi.snes_no_lag_t noLagCb, BsnesApi.snes_video_frame_t videoFrameCb,
			BsnesApi.snes_audio_sample_t audioSampleCb, BsnesApi.snes_path_request_t pathRequestCb);
		[BizImport(CallingConvention.Cdecl)] // fuck this shit
		public abstract void snes_set_callbacks2(BsnesApi.snes_trace_t traceCb);

		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_init(BsnesApi.ENTROPY entropy, BsnesApi.BSNES_INPUT_DEVICE left, BsnesApi.BSNES_INPUT_DEVICE right, bool hotfixes, bool fastPPU);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_power();
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_term();
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_reset();
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_run();

		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_serialize(byte[] serializedData, int serializedSize);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_unserialize(byte[] serializedData, int serializedSize);
		[BizImport(CallingConvention.Cdecl)]
		public abstract int snes_serialized_size();

		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_load_cartridge_normal(string baseRomPath, byte[] romData, int romSize);
		[BizImport(CallingConvention.Cdecl)]
		public abstract void snes_load_cartridge_super_gameboy(string baseRomPath, byte[] romData, int romSize, byte[] sgbRomData, int sgbRomSize);
	}

	public unsafe partial class BsnesApi : IDisposable, IMonitor, IStatable
	{
		static BsnesApi()
		{
			if (sizeof(CommStruct) != 368)
			{
				throw new InvalidOperationException("sizeof(comm)");
			}
		}

		internal WaterboxHost _exe;
		internal BsnesCoreImpl _core;
		private bool _disposed;
		private bool _sealed;

		public void Enter()
		{
			_exe.Enter();
		}

		public void Exit()
		{
			_exe.Exit();
		}

		private readonly List<string> _readonlyFiles = new List<string>();

		public void AddReadonlyFile(byte[] data, string name)
		{
			_exe.AddReadonlyFile(data, name);
			_readonlyFiles.Add(name);
		}

		public BsnesApi(BsnesCore core, string dllPath, CoreComm comm, IEnumerable<Delegate> allCallbacks)
		{
			_exe = new WaterboxHost(new WaterboxOptions
			{
				Filename = "bsnes.wbx",
				Path = dllPath,
				SbrkHeapSizeKB = 16 * 1024, // TODO: this can probably be optimized slightly
				InvisibleHeapSizeKB = 4,
				MmapHeapSizeKB = 128 * 1024, // TODO: check whether this can be smaller; it needs to be 80+ at least rn
				PlainHeapSizeKB = 0,
				SealedHeapSizeKB = 0, // this might actually need to be larger than 0, but doesn't crash for me
				SkipCoreConsistencyCheck = comm.CorePreferences.HasFlag(CoreComm.CorePreferencesFlags.WaterboxCoreConsistencyCheck),
				SkipMemoryConsistencyCheck = comm.CorePreferences.HasFlag(CoreComm.CorePreferencesFlags.WaterboxMemoryConsistencyCheck),
			});
			using (_exe.EnterExit())
			{
				// Marshal checks that function pointers passed to GetDelegateForFunctionPointer are
				// _currently_ valid when created, even though they don't need to be valid until
				// the delegate is later invoked.  so GetInvoker needs to be acquired within a lock.
				_core = BizInvoker.GetInvoker<BsnesCoreImpl>(_exe, _exe, CallingConventionAdapters.MakeWaterbox(allCallbacks, _exe));
				_core.DllInit();
			}
		}

		public void Dispose()
		{
			if (!_disposed)
			{
				_disposed = true;
				_exe.Dispose();
				_exe = null;
				_core = null;
			}
		}

		private snes_scanlineStart_t scanlineStart;
		private snes_trace_t traceCallback;


		public delegate void snes_video_frame_t(ushort* data, int width, int height, int pitch);
		public delegate void snes_input_poll_t();
		public delegate short snes_input_state_t(int port, int device, int index, int id);
		public delegate void snes_no_lag_t();
		public delegate void snes_audio_sample_t(short left, short right);
		public delegate void snes_scanlineStart_t(int line);
		public delegate string snes_path_request_t(int slot, string hint);
		public delegate void snes_trace_t(string msg);


		[StructLayout(LayoutKind.Sequential, Pack = 1)]
		public struct CPURegs
		{
			public uint pc;
			public ushort a, x, y, s, d, vector; //7x
			public byte p, db, nothing, nothing2;
			public ushort v, h;
		}

		// I cannot use a struct here because marshalling is retarded for bool (4 bytes). I honestly cannot
		[StructLayout(LayoutKind.Sequential)]
		public class LayerEnables
		{
			public bool BG1_Prio0, BG1_Prio1;
			public bool BG2_Prio0, BG2_Prio1;
			public bool BG3_Prio0, BG3_Prio1;
			public bool BG4_Prio0, BG4_Prio1;
			public bool Obj_Prio0, Obj_Prio1, Obj_Prio2, Obj_Prio3;
		}

		[StructLayout(LayoutKind.Sequential)]
		public class SnesCallbacks
		{
			public snes_input_poll_t inputPollCb;
			public snes_input_state_t inputStateCb;
			public snes_no_lag_t noLagCb;
			public snes_video_frame_t videoFrameCb;
			public snes_audio_sample_t audioSampleCb;
			public snes_path_request_t pathRequestCb;
			public snes_trace_t snesTraceCb;
		}

		[StructLayout(LayoutKind.Explicit)]
		private struct CommStruct
		{
			[FieldOffset(0)]
			//the cmd being executed
			public readonly eMessage cmd;
			[FieldOffset(4)]
			//the status of the core
			public readonly eStatus status;
			[FieldOffset(8)]
			//the SIG or BRK that the core is halted in
			public readonly eMessage reason;

			//flexible in/out parameters
			//these are all "overloaded" a little so it isn't clear what's used for what in for any particular message..
			//but I think it will beat having to have some kind of extremely verbose custom layouts for every message
			[FieldOffset(16)]
			public sbyte* str;
			[FieldOffset(24)]
			public void* ptr;
			[FieldOffset(32)]
			public uint id;
			[FieldOffset(36)]
			public uint addr;
			[FieldOffset(40)]
			public uint value;
			[FieldOffset(44)]
			public uint size;
			[FieldOffset(48)]
			public int port;
			[FieldOffset(52)]
			public int device;
			[FieldOffset(56)]
			public int index;
			[FieldOffset(60)]
			public int slot;
			[FieldOffset(64)]
			public int width;
			[FieldOffset(68)]
			public int height;
			[FieldOffset(72)]
			public int scanline;
			[FieldOffset(76)]
			public fixed int inports[2];

			[FieldOffset(88)]
			//this should always be used in pairs
			public fixed long buf[3]; //ACTUALLY A POINTER but can't marshal it :(
			[FieldOffset(112)]
			public fixed int buf_size[3];

			[FieldOffset(128)]
			//bleck. this is a long so that it can be a 32/64bit pointer
			public fixed long cdl_ptr[16];
			[FieldOffset(256)]
			public fixed int cdl_size[16];

			[FieldOffset(320)]
			public CPURegs cpuregs;
			// [FieldOffset(344)]
			// public LayerEnables layerEnables;

			[FieldOffset(356)]
			//static configuration-type information which can be grabbed off the core at any time without even needing a QUERY command
			public SNES_REGION region;
			[FieldOffset(360)]
			public SNES_MAPPER mapper;

			[FieldOffset(364)] private uint BLANK0;


			//utilities
			//TODO: make internal, wrap on the API instead of the comm
			// gotcha m8 we gonna nuke the comm struct
		}

		public void Seal()
		{
			/* Cothreads can very easily acquire "pointer poison"; because their stack and even registers
			 * are part of state, any poisoned pointer that's used even temporarily might be persisted longer
			 * than needed.  Most of the libsnes core cothreads handle internal matters only and aren't very
			 * vulnerable to pointer poison, but the main boss cothread is used heavily during init, when
			 * many syscalls happen and many kinds of poison can end up on the stack.  so here, we call
			 * _core.DllInit() again, which recreates that cothread, zeroing out all of the memory first,
			 * as well as zeroing out the comm struct. */

			//TODO: well check this yknow
			// let's see what happen when we don't attempt to hack it like that
			// _core.DllInit();
			_exe.Seal();
			_sealed = true;
			foreach (var s in _readonlyFiles)
			{
				_exe.RemoveReadonlyFile(s);
			}
			_readonlyFiles.Clear();
		}

		// TODO: confirm that the serializedSize is CONSTANT for any given game,
		// else this might be problematic
		private int serializedSize;// = 284275;

		public void SaveStateBinary(BinaryWriter writer)
		{
			if (serializedSize == 0)
				serializedSize = _core.snes_serialized_size();
			// TODO: do some profiling and testing to check whether this is actually better than _exe.SaveStateBinary(writer);

			byte[] serializedData = new byte[serializedSize];
			_core.snes_serialize(serializedData, serializedSize);
			writer.Write(serializedData);
		}

		public void LoadStateBinary(BinaryReader reader)
		{
			byte[] serializedData = reader.ReadBytes(serializedSize);
			_core.snes_unserialize(serializedData, serializedSize);
		}
	}
}
