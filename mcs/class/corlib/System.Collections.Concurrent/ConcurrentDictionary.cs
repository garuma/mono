// SplitOrderedList.cs
//
// Copyright (c) 2012 Xamarin, Inc.
//
// Author: Jeremie Laval <jeremie.laval@gmail.com>
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//

#if NET_4_0

using System;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Diagnostics;

namespace System.Collections.Concurrent
{
	// Hopscotch algorithm
	[DebuggerDisplay ("Count={Count}")]
	[DebuggerTypeProxy (typeof (CollectionDebuggerView<,>))]
	public class ConcurrentDictionary<TKey, TValue> : IDictionary<TKey, TValue>,
		ICollection<KeyValuePair<TKey, TValue>>, IEnumerable<KeyValuePair<TKey, TValue>>,
		IDictionary, ICollection, IEnumerable
	{
		// Log 2 table
		static readonly byte[] logTable = {
			0xFF, 0, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4,
			4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
			5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6,
			6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
			6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6,
			6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7,
			7, 7, 7, 7
		};
		// map a bit value mod 37 to its position
		static readonly int[] Mod37BitPosition = {
			32, 0, 1, 26, 2, 23, 27, 0, 3, 16, 24, 30, 28, 11, 0, 13, 4,
			7, 17, 0, 25, 22, 31, 15, 29, 10, 12, 6, 0, 21, 14, 9, 5,
			20, 8, 19, 18
		};

		const int HopRange = 32;
		const int AddRange = 64;
		const int MaxSegments = 1 << 16;
		const int MaxTries = 2;
		const int DefaultCapacity = 96;

		class Bucket
		{
			public volatile uint HopInfo;
			public volatile uint Timestamp;
			public AtomicBooleanValue Busy;

			public TKey Key;
			public TValue Value;

			//public SpinLock Lock;
			public object Lock = new object ();

			public Bucket ()
			{
			}

			public Bucket (Bucket other)
			{
				HopInfo = 1;
				Busy.Value = true;
				Key = other.Key;
				Value = other.Value;
			}
		}

		int capacity;
		Bucket[][] segmentsArray = new Bucket[MaxSegments][];
		IEqualityComparer<TKey> keyComparer;
		int segmentMask;
		int bucketMask;
		int count;
		int segmentShift;

		SimpleRwLock resizeLock = new SimpleRwLock ();

		private ConcurrentDictionary (IEqualityComparer<TKey> comparer, int capacity)
		{
			this.keyComparer = comparer;
			var closestPowerOfTwo = ClosestPowerOfTwo ((uint)Math.Max (HopRange, capacity));
			this.capacity = 1 << closestPowerOfTwo;
			segmentShift = Math.Min (32 - closestPowerOfTwo, 0);
			segmentsArray[0] = InitBucketSegment (this.capacity);
			bucketMask = this.capacity - 1;
			segmentMask = 0;
		}

		public ConcurrentDictionary () : this (EqualityComparer<TKey>.Default, DefaultCapacity)
		{
		}

		public ConcurrentDictionary (IEnumerable<KeyValuePair<TKey, TValue>> collection)
			: this (collection, EqualityComparer<TKey>.Default)
		{
		}

		public ConcurrentDictionary (IEqualityComparer<TKey> comparer) : this (comparer, DefaultCapacity)
		{
		}

		public ConcurrentDictionary (IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer)
			: this (comparer)
		{
			foreach (KeyValuePair<TKey, TValue> pair in collection)
				Add (pair.Key, pair.Value);
		}

		public ConcurrentDictionary (int concurrencyLevel, int capacity)
			: this (EqualityComparer<TKey>.Default, capacity)
		{

		}

		public ConcurrentDictionary (int concurrencyLevel,
		                             IEnumerable<KeyValuePair<TKey, TValue>> collection,
		                             IEqualityComparer<TKey> comparer)
			: this (collection, comparer)
		{

		}

		public ConcurrentDictionary (int concurrencyLevel, int capacity, IEqualityComparer<TKey> comparer)
			: this (comparer, capacity * concurrencyLevel)
		{

		}

		void CheckKey (TKey key)
		{
			if (key == null)
				throw new ArgumentNullException ("key");
		}

		public bool TryGetValue (TKey key, out TValue value)
		{
			value = default (TValue);
			CheckKey (key);
			Bucket bucket;
			bool result = FindBucket (key, out bucket);
			if (result)
				value = bucket.Value;
			return result;
		}

		bool FindBucket (TKey key, out Bucket bucket)
		{
			bucket = null;
			var hash = ExtraHash (keyComparer.GetHashCode (key));
			var iSegment = (hash >> segmentShift) & segmentMask;
			var segment = segmentsArray[iSegment];
			var startBucketIndex = hash & bucketMask;
			var startBucket = segment[startBucketIndex];
			int tryCounter = 0;
			int hopRange = Math.Min (HopRange, segment.Length - startBucketIndex);
			uint timestamp;

			do {
				timestamp = startBucket.Timestamp;
				uint hopInfo = startBucket.HopInfo;
				for (int i = 0; i < hopRange; i++) {
					if (((hopInfo >> i) & 1) == 0)
						continue;
					var checkBucket = segment[startBucketIndex + i];
					if (keyComparer.Equals (key, checkBucket.Key)) {
						bucket = checkBucket;
						return true;
					}
				}
				++tryCounter;
			} while (timestamp != startBucket.Timestamp && tryCounter < MaxTries);

			if (timestamp != startBucket.Timestamp) {
				var checkBucketIndex = startBucketIndex;
				var checkBucket = segment[checkBucketIndex];
				for (int i = 0; i < hopRange; ++i) {
					if (keyComparer.Equals (key, checkBucket.Key)) {
						bucket = checkBucket;
						return true;
					}
					checkBucket = segment[++checkBucketIndex];
				}
			}
			return false;
		}

		bool SuperTry (TKey key,
		               TValue value, Func<TKey, TValue> valueCreator, // The add possibilities
		               bool hasUpdateValue, TValue updateValue, Func<TKey, TValue, TValue> updateValueFactory, // The update possibilities
		               out TValue presentValue) // Getting back the final value
		{
			presentValue = default (TValue);
			try {
				resizeLock.EnterReadLock ();

				var hash = ExtraHash (keyComparer.GetHashCode (key));
				var iSegment = (hash >> segmentShift) & segmentMask;
				var iBucket = hash & bucketMask;
				var segment = segmentsArray[iSegment];
				var startBucket = segment[iBucket];
				Console.WriteLine ("{0} -> {1} : {2} ({3})", hash.ToString (), iSegment.ToString (), iBucket.ToString (), segmentMask.ToString ());

				lock (startBucket.Lock) {
					Bucket bucket;
					if (FindBucket (key, out bucket)) {
						if (updateValueFactory != null || hasUpdateValue)
							presentValue = bucket.Value = (hasUpdateValue ? updateValue : updateValueFactory (key, bucket.Value));
						else
							presentValue = bucket.Value;
						return false;
					}

					Bucket freeBucket = startBucket;
					int currentBucketIndex = iBucket;
					var addRange = Math.Min (AddRange, segment.Length - currentBucketIndex);
					int freeDistance = 0;
					for (; freeDistance < addRange; ++freeDistance) {
						if (freeBucket.Busy.TryRelaxedSet ())
							break;
						freeBucket = segment[Math.Min (++currentBucketIndex, segment.Length - 1)];
					}
					if (freeDistance < addRange) {
						do {
							if (freeDistance < HopRange) {
								startBucket.HopInfo |= (1u << freeDistance);
								//Console.WriteLine (startBucket.HopInfo);
								freeBucket.Value = presentValue = valueCreator == null ? value : valueCreator (key);
								freeBucket.Key = key;
								Interlocked.Increment (ref count);
								return true;
							}
							FindCloserFreeBucket (ref freeBucket, segment, currentBucketIndex, ref freeDistance);
						} while (freeBucket != null);
					}
				}
			} finally {
				resizeLock.ExitReadLock ();
			}
			Resize ();
			return SuperTry (key, value, valueCreator, hasUpdateValue, updateValue, updateValueFactory, out presentValue);
		}

		public bool TryRemove (TKey key, out TValue value)
		{
			value = default (TValue);
			CheckKey (key);
			try {
				resizeLock.EnterReadLock ();

				var hash = ExtraHash (keyComparer.GetHashCode (key));
				var iSegment = (hash >> segmentShift) & segmentMask;
				var segment = segmentsArray[iSegment];
				var startBucketIndex = hash & bucketMask;
				var startBucket = segment[startBucketIndex];
				//Console.WriteLine ("R {0} -> {1} : {2} ({3}) -> {4}", hash.ToString (), iSegment.ToString (), startBucketIndex.ToString (), segmentMask.ToString (), startBucket.Key);

				lock (startBucket.Lock) {
					uint hopInfo = startBucket.HopInfo;
					int hopRange = Math.Min (HopRange, segment.Length - startBucketIndex);
					for (int i = 0; i < hopRange; ++i) {
						if (((hopInfo >> i) & 1) == 0)
							continue;
						var checkBucket = segment[startBucketIndex + i];
						//Console.WriteLine ("\tCheck {0} {1}", i.ToString (), checkBucket.Key);
						if (keyComparer.Equals (key, checkBucket.Key)) {
							value = checkBucket.Value;

							Thread.MemoryBarrier ();

							checkBucket.Value = default (TValue);
							checkBucket.Key = default (TKey);

							Thread.MemoryBarrier ();

							checkBucket.Busy.Value = false;
							startBucket.HopInfo &= ~(1u << i);
							Interlocked.Decrement (ref count);
							return true;
						}
					}
				}
				return false;
			} finally {
				resizeLock.ExitReadLock ();
			}
		}

		void Add (TKey key, TValue value)
		{
			while (!TryAdd (key, value));
		}

		void IDictionary<TKey, TValue>.Add (TKey key, TValue value)
		{
			CheckKey (key);
			Add (key, value);
		}

		public bool TryAdd (TKey key, TValue value)
		{
			CheckKey (key);
			TValue dummy;
			return SuperTry (key, value, null, false, default (TValue), null, out dummy);
		}

		void ICollection<KeyValuePair<TKey,TValue>>.Add (KeyValuePair<TKey, TValue> pair)
		{
			CheckKey (pair.Key);
			Add (pair.Key, pair.Value);
		}

		public TValue AddOrUpdate (TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
		{
			CheckKey (key);
			TValue final;
			SuperTry (key, default (TValue), addValueFactory, false, default (TValue), updateValueFactory, out final);
			return final;
		}

		public TValue AddOrUpdate (TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
		{
			CheckKey (key);
			TValue final;
			SuperTry (key, addValue, null, false, default(TValue), updateValueFactory, out final);
			return final;
		}

		void AddOrUpdate (TKey key, TValue newValue)
		{
			CheckKey (key);
			TValue dummy;
			SuperTry (key, newValue, null, true, newValue, null, out dummy);
		}

		TValue GetValue (TKey key)
		{
			CheckKey (key);
			TValue temp;
			if (!TryGetValue (key, out temp))
				throw new KeyNotFoundException (key.ToString ());
			return temp;
		}

		// TODO: see for race with adding/removing
		public bool TryUpdate (TKey key, TValue newValue, TValue comparisonValue)
		{
			CheckKey (key);

			Bucket bucket;
			bool result = FindBucket (key, out bucket);
			if (!result)
				return false;
			lock (bucket) {
				result = (bucket.Value == null && comparisonValue == null) || bucket.Value.Equals (comparisonValue);
				if (result)
					bucket.Value = newValue;
			}
			return result;
		}

		public TValue this[TKey key] {
			get {
				return GetValue (key);
			}
			set {
				AddOrUpdate (key, value);
			}
		}

		public TValue GetOrAdd (TKey key, Func<TKey, TValue> valueFactory)
		{
			CheckKey (key);
			TValue presentValue;
			SuperTry (key, default (TValue), valueFactory, false, default (TValue), null, out presentValue);
			return presentValue;
		}

		public TValue GetOrAdd (TKey key, TValue value)
		{
			CheckKey (key);
			TValue presentValue;
			SuperTry (key, value, null, false, default (TValue), null, out presentValue);
			return presentValue;
		}

		bool Remove (TKey key)
		{
			CheckKey (key);
			TValue dummy;
			return TryRemove (key, out dummy);
		}

		bool IDictionary<TKey, TValue>.Remove (TKey key)
		{
			return Remove (key);
		}

		bool ICollection<KeyValuePair<TKey,TValue>>.Remove (KeyValuePair<TKey,TValue> pair)
		{
			return Remove (pair.Key);
		}

		public bool ContainsKey (TKey key)
		{
			CheckKey (key);
			TValue dummy;
			return TryGetValue (key, out dummy);
		}

		bool IDictionary.Contains (object key)
		{
			if (!(key is TKey))
				return false;

			return ContainsKey ((TKey)key);
		}

		void IDictionary.Remove (object key)
		{
			if (!(key is TKey))
				return;

			Remove ((TKey)key);
		}

		object IDictionary.this [object key]
		{
			get {
				if (!(key is TKey))
					throw new ArgumentException ("key isn't of correct type", "key");

				return this[(TKey)key];
			}
			set {
				if (!(key is TKey) || !(value is TValue))
					throw new ArgumentException ("key or value aren't of correct type");

				this[(TKey)key] = (TValue)value;
			}
		}

		void IDictionary.Add (object key, object value)
		{
			if (!(key is TKey) || !(value is TValue))
				throw new ArgumentException ("key or value aren't of correct type");

			Add ((TKey)key, (TValue)value);
		}

		bool ICollection<KeyValuePair<TKey,TValue>>.Contains (KeyValuePair<TKey, TValue> pair)
		{
			return ContainsKey (pair.Key);
		}

		public KeyValuePair<TKey,TValue>[] ToArray ()
		{
			// This is most certainly not optimum but there is
			// not a lot of possibilities
			return new List<KeyValuePair<TKey,TValue>> (this).ToArray ();
		}

		public void Clear()
		{
			// TODO
		}

		public int Count {
			get {
				return count;
			}
		}

		public bool IsEmpty {
			get {
				return Count == 0;
			}
		}

		bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly {
			get {
				return false;
			}
		}

		bool IDictionary.IsReadOnly {
			get {
				return false;
			}
		}

		public ICollection<TKey> Keys {
			get {
				return GetPart<TKey> ((kvp) => kvp.Key);
			}
		}

		public ICollection<TValue> Values {
			get {
				return GetPart<TValue> ((kvp) => kvp.Value);
			}
		}

		ICollection IDictionary.Keys {
			get {
				return (ICollection)Keys;
			}
		}

		ICollection IDictionary.Values {
			get {
				return (ICollection)Values;
			}
		}

		ICollection<T> GetPart<T> (Func<KeyValuePair<TKey, TValue>, T> extractor)
		{
			List<T> temp = new List<T> ();

			foreach (KeyValuePair<TKey, TValue> kvp in this)
				temp.Add (extractor (kvp));

			return temp.AsReadOnly ();
		}

		void ICollection.CopyTo (Array array, int startIndex)
		{
			KeyValuePair<TKey, TValue>[] arr = array as KeyValuePair<TKey, TValue>[];
			if (arr == null)
				return;

			CopyTo (arr, startIndex, Count);
		}

		void CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex)
		{
			CopyTo (array, startIndex, Count);
		}

		void ICollection<KeyValuePair<TKey, TValue>>.CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex)
		{
			CopyTo (array, startIndex);
		}

		void CopyTo (KeyValuePair<TKey, TValue>[] array, int startIndex, int num)
		{
			foreach (var kvp in this) {
				array [startIndex++] = kvp;

				if (--num <= 0)
					return;
			}
		}

		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator ()
		{
			return GetEnumeratorInternal ();
		}

		IEnumerator IEnumerable.GetEnumerator ()
		{
			return (IEnumerator)GetEnumeratorInternal ();
		}

		IEnumerator<KeyValuePair<TKey, TValue>> GetEnumeratorInternal ()
		{
			var segCount = segmentMask + 1;
			for (int seg = 0; seg < segCount; seg++) {
				var segment = segmentsArray[seg];
				for (int i = 0; i < segment.Length; i++) {
					var bucket = segment[i];
					var key = bucket.Key;
					var value = bucket.Value;
					//if (bucket.Busy.Value && bucket.Ready)
					if (bucket.Busy.Value)
						yield return new KeyValuePair<TKey, TValue> (key, value);
				}
			}
		}

		IDictionaryEnumerator IDictionary.GetEnumerator ()
		{
			return new ConcurrentDictionaryEnumerator (GetEnumeratorInternal ());
		}

		class ConcurrentDictionaryEnumerator : IDictionaryEnumerator
		{
			IEnumerator<KeyValuePair<TKey, TValue>> internalEnum;

			public ConcurrentDictionaryEnumerator (IEnumerator<KeyValuePair<TKey, TValue>> internalEnum)
			{
				this.internalEnum = internalEnum;
			}

			public bool MoveNext ()
			{
				return internalEnum.MoveNext ();
			}

			public void Reset ()
			{
				internalEnum.Reset ();
			}

			public object Current {
				get {
					return Entry;
				}
			}

			public DictionaryEntry Entry {
				get {
					KeyValuePair<TKey, TValue> current = internalEnum.Current;
					return new DictionaryEntry (current.Key, current.Value);
				}
			}

			public object Key {
				get {
					return internalEnum.Current.Key;
				}
			}

			public object Value {
				get {
					return internalEnum.Current.Value;
				}
			}
		}

		object ICollection.SyncRoot {
			get {
				return this;
			}
		}

		bool IDictionary.IsFixedSize {
			get {
				return false;
			}
		}

		bool ICollection.IsSynchronized {
			get { return true; }
		}

		int ClosestPowerOfTwo (uint v)
		{
			uint t, tt;
			var pos = (tt = v >> 16) > 0 ?
				(t = tt >> 8) > 0 ? 24 + logTable[t] : 16 + logTable[tt] :
				(t = v >> 8) > 0 ? 8 + logTable[t] : logTable[v];
			return pos;
		}

		void FindCloserFreeBucket (ref Bucket freeBucket, Bucket[] segment, int freeBucketIndex, ref int freeDistance)
		{
			Console.WriteLine ("FindCloserFreeBucket");
			var moveBucketIndex = freeBucketIndex - (HopRange - 1);
			var moveBucket = segment[moveBucketIndex];
			for (int freeDist = (HopRange - 1); freeDist > 0; --freeDist) {
				uint startHopInfo = moveBucket.HopInfo;
				//int moveFreeDistance = RightTrailingZeroCount (startHopInfo);
				int moveFreeDistance = -1;
				uint mask = 1;
				for (int i = 0; i < freeDist; ++i, mask <<= 1) {
					if ((mask & startHopInfo) > 0) {
						moveFreeDistance = i;
						break;
					}
				}
				if (moveFreeDistance != -1) {
					lock (moveBucket.Lock) {
						if (startHopInfo == moveBucket.HopInfo) {
							var newFreeBucket = segment[moveBucketIndex + moveFreeDistance];
							moveBucket.HopInfo |= (1u << freeDist);
							Thread.MemoryBarrier ();
							freeBucket.Value = newFreeBucket.Value;
							freeBucket.Key = newFreeBucket.Key;
							Thread.MemoryBarrier ();
							++moveBucket.Timestamp;

							newFreeBucket.Busy.Value = true;
							newFreeBucket.Key = default (TKey);
							newFreeBucket.Value = default (TValue);
							moveBucket.HopInfo &= ~(1u << moveFreeDistance);

							freeBucket = newFreeBucket;
							freeDistance -= freeDist;
							return;
						}
					}
				}
				moveBucket = segment[++moveBucketIndex];
			}
			freeBucket = null;
			freeDistance = 0;
		}

		int RightTrailingZeroCount (uint v)
		{
			return Mod37BitPosition[(-v & v) % 37];
		}

		void Resize ()
		{
			try {
				resizeLock.EnterWriteLock ();
				//Console.WriteLine ("Resize");
				var segCount = segmentMask + 1;
				var newSegCount = segCount << 1;
				var newSegMask = newSegCount - 1;
				if (newSegCount > MaxSegments)
					throw new OverflowException ("We can't create more segments");

				// Add the extra segments
				for (int i = segCount; i < newSegCount; i++)
					segmentsArray[i] = InitBucketSegment (capacity);

				// Rehash existing keys
				for (int i = 0; i < segCount; i++) {
					var seg = segmentsArray[i];
					for (int j = 0; j < seg.Length; j++) {
						var bucket = seg[j];
						if (!bucket.Busy.Value)
							continue;
						var hash = ExtraHash (keyComparer.GetHashCode (bucket.Key));
						// We only attempt to set an element if it has a bit set at the new position
						if ((hash & (newSegMask - segmentMask)) > 0) {
							var newSegIndex = (hash >> segmentShift) & newSegMask;
							//Console.WriteLine ("M {0} {1} @{4}: {2} -> {3}", hash.ToString (), bucket.Key.ToString (), i.ToString (), newSegIndex.ToString (), j.ToString ());
							var newSeg = segmentsArray[newSegIndex];
							// We make a copy of the bucket because concurrent read needs to still happen
							newSeg[j] = new Bucket (bucket);
							// We mark the old bucket as reusable
							bucket.Busy.Value = false;
							// If our position was hop'ed we register ourselves in our new parent hop info
							var normalPosition = hash & bucketMask;
							if (j != normalPosition)
								newSeg[normalPosition].HopInfo |= (1u << (j - normalPosition));
						}
					}
				}
				segmentMask = newSegMask;
			} finally {
				resizeLock.ExitWriteLock ();
			}
		}

		Bucket[] InitBucketSegment (int capacity)
		{
			var result = new Bucket[capacity];
			for (int i = 0; i < result.Length; i++)
				result[i] = new Bucket ();
			return result;
		}

		static int ExtraHash (int h)
		{
			// Spread bits to regularize both segment and index locations,
			// using variant of single-word Wang/Jenkins hash.
			uint hh = (uint)h;
			hh += (hh << 15) ^ 0xffffcd7d;
			hh ^= (hh >> 10);
			hh += (hh <<  3);
			hh ^= (hh >>  6);
			hh += (hh <<  2) + (hh << 14);
			return (int)(hh ^ (hh >> 16));
		}

		struct SimpleRwLock
		{
			const int RwWait = 1;
			const int RwWrite = 2;
			const int RwRead = 4;

			int rwlock;

			public void EnterReadLock ()
			{
				SpinWait sw = new SpinWait ();
				do {
					while ((rwlock & (RwWrite | RwWait)) > 0)
						sw.SpinOnce ();

					if ((Interlocked.Add (ref rwlock, RwRead) & (RwWait | RwWait)) == 0)
						return;

					Interlocked.Add (ref rwlock, -RwRead);
				} while (true);
			}

			public void ExitReadLock ()
			{
				Interlocked.Add (ref rwlock, -RwRead);
			}

			public void EnterWriteLock ()
			{
				SpinWait sw = new SpinWait ();
				do {
					int state = rwlock;
					if (state < RwWrite) {
						if (Interlocked.CompareExchange (ref rwlock, RwWrite, state) == state)
							return;
						state = rwlock;
					}

					while ((state & RwWait) == 0 && Interlocked.CompareExchange (ref rwlock, state | RwWait, state) != state)
						state = rwlock;
					// Before falling to sleep
					while (rwlock > RwWait)
						sw.SpinOnce ();
				} while (true);
			}

			public void ExitWriteLock ()
			{
				Interlocked.Add (ref rwlock, -RwWrite);
			}
		}
	}
}

#endif
