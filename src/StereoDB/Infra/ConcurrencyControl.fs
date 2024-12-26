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
    
    let mutable _currentTxThreadId = 0L
    let mutable _readTxCounter = 0L
    let mutable _readTxHistoryCounter = 0L
    let CPUCores = Environment.ProcessorCount
    
    let enterWriteLock threadId =
        let spin = SpinWait()
        let mutable lockAcquired = false        
        
        while not lockAcquired do
            let currentThreadId = Interlocked.Read(&_currentTxThreadId)
            
            if Interlocked.CompareExchange(&_currentTxThreadId, threadId, 0) = 0 then
                lockAcquired <- true
            
            elif currentThreadId < 0 && Interlocked.Read(&_readTxCounter) = 0 && Interlocked.Read(&_readTxHistoryCounter) >= CPUCores then
                if Interlocked.CompareExchange(&_currentTxThreadId, threadId, currentThreadId) = currentThreadId then
                    lockAcquired <- true
                    Interlocked.Exchange(&_readTxHistoryCounter, 0) |> ignore
            else
                spin.SpinOnce()
    
    let releaseWriteLock threadId =
        if Interlocked.CompareExchange(&_currentTxThreadId, 0, threadId) <> threadId then
            failwith "release write lock failed"
    
    let enterReadLock threadId =
        let spin = SpinWait()
        let mutable lockAcquired = false
        let mutable skipSpin = false
        let mutable skipTryCount = 0
        
        while not lockAcquired do            
            let currentThreadId = Interlocked.Read(&_currentTxThreadId)            
            
            if currentThreadId < 0 then
                if Interlocked.Read(&_readTxHistoryCounter) < CPUCores
                   && Interlocked.CompareExchange(&_currentTxThreadId, threadId, currentThreadId) = currentThreadId then
                    lockAcquired <- true
                
                elif Interlocked.Read(&_readTxCounter) = 0 && Interlocked.Read(&_readTxHistoryCounter) >= CPUCores then                
                    if Interlocked.CompareExchange(&_currentTxThreadId, threadId, currentThreadId) = currentThreadId then
                        lockAcquired <- true
                        Interlocked.Exchange(&_readTxHistoryCounter, 0) |> ignore
                else
                    skipSpin <- true
                    skipTryCount <- skipTryCount + 1
            
            elif Interlocked.CompareExchange(&_currentTxThreadId, threadId, 0) = 0 then
                lockAcquired <- true
            
            if lockAcquired then
                Interlocked.Increment(&_readTxHistoryCounter) |> ignore
                Interlocked.Increment(&_readTxCounter) |> ignore                
            elif skipSpin && skipTryCount <= 1 then
                ()
            else
                spin.SpinOnce()
                skipTryCount <- 0
                skipSpin <- false
    
    let releaseReadLock threadId =
        Interlocked.Decrement(&_readTxCounter) |> ignore
        Interlocked.CompareExchange(&_currentTxThreadId, 0, threadId) |> ignore
    
    interface IThreadLock with
        member this.EnterWriteLock(threadId) = enterWriteLock threadId            
        member this.ReleaseWriteLock(threadId) = releaseWriteLock threadId            
        
        member this.EnterReadLock(threadId) = enterReadLock -threadId     // we mark readTxId with a minus        
        member this.ReleaseReadLock(threadId) = releaseReadLock -threadId // we mark readTxId with a minus