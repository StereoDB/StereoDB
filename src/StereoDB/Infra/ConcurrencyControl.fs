module internal StereoDB.Infra.ConcurrencyControl

open System
open System.Threading

type IThreadLock =
    abstract EnterWriteLock: threadId:int64 -> unit
    abstract ReleaseWriteLock: threadId:int64 -> unit
    abstract EnterReadLock: threadId:int64 -> unit    
    abstract ReleaseReadLock: threadId:int64 -> unit

type RwLockSlimLock() =
    
    let _lockSlim = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion)
    
    interface IThreadLock with
        member this.EnterWriteLock(threadId) = _lockSlim.EnterWriteLock()
        member this.ReleaseWriteLock(threadId) = _lockSlim.ExitWriteLock()

        member this.EnterReadLock(threadId) = _lockSlim.EnterReadLock()
        member this.ReleaseReadLock(threadId) = _lockSlim.ExitReadLock()
        
type CasSpinLock() =
    
    let mutable _currentTxThreadId = 0L
    
    let enterLock threadId =
        let spin = SpinWait()
        let mutable lockAcquired = false        
        
        while not lockAcquired do
            if Interlocked.CompareExchange(&_currentTxThreadId, threadId, 0) = 0 then
                lockAcquired <- true                
            else
                spin.SpinOnce()
                
    let releaseLock threadId =
        if Interlocked.CompareExchange(&_currentTxThreadId, 0, threadId) <> threadId then
            failwith "release write lock failed"
    
    interface IThreadLock with
        member this.EnterWriteLock(threadId) = enterLock threadId
        member this.ReleaseWriteLock(threadId) = releaseLock threadId
        
        member this.EnterReadLock(threadId) = enterLock threadId
        member this.ReleaseReadLock(threadId) = releaseLock threadId

type CasRwSpinLock() =
    let mutable _readersCount = 0L  // Tracks the number of active readers
    let mutable _writerLock = 0L    // Indicates if a writer holds the lock (1 = locked, 0 = unlocked)
    let mutable _writerWaiting = 0L // Indicates if a writer is waiting (1 = waiting, 0 = not waiting)

    let enterReadLock () =
        let spin = SpinWait()
        let mutable lockAcquired = false
        
        while not lockAcquired do
            // Check if a writer is waiting
            if Interlocked.Read(&_writerWaiting) = 0L then
                // Try to increment readersCount
                Interlocked.Increment(&_readersCount) |> ignore
                // Verify that no writer became active after incrementing
                if Interlocked.Read(&_writerWaiting) = 0L then
                    lockAcquired <- true
                else
                    // A writer is waiting; decrement readersCount and retry
                    Interlocked.Decrement(&_readersCount) |> ignore
                    spin.SpinOnce()
            else
                // A writer is waiting; spin and retry
                spin.SpinOnce()
    
    let releaseReadLock () =
        // Decrement the readers count
        if Interlocked.Decrement(&_readersCount) < 0L then
            failwith "release read lock failed - invalid state"
    
    let enterWriteLock threadId =
        let spin = SpinWait()
        let mutable lockAcquired = false

        // Wait until no other writer is waiting
        while Interlocked.CompareExchange(&_writerWaiting, 1L, 0L) <> 0L do
            spin.SpinOnce()

        while not lockAcquired do
            // Attempt to acquire the writer lock
            if Interlocked.CompareExchange(&_writerLock, threadId, 0L) = 0L then
                // Ensure all readers have completed
                while Interlocked.Read(&_readersCount) > 0L do
                    spin.SpinOnce()
                lockAcquired <- true
            else
                spin.SpinOnce()
    
    let releaseWriteLock threadId =
        // Release the writer lock
        if Interlocked.CompareExchange(&_writerLock, 0L, threadId) <> threadId then
            failwith "release write lock failed - lock not held by thread"
        // Clear writer intent
        Interlocked.Exchange(&_writerWaiting, 0L) |> ignore

    interface IThreadLock with
        member this.EnterReadLock(threadId) = enterReadLock()
        member this.ReleaseReadLock(threadId) = releaseReadLock()
        member this.EnterWriteLock(threadId) = enterWriteLock threadId
        member this.ReleaseWriteLock(threadId) = releaseWriteLock threadId