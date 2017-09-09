﻿using System;
using System.Threading;
using System.Runtime.CompilerServices;

namespace AutoCSer.Net.TcpSimpleServer
{
    /// <summary>
    /// 客户端心跳检测定时
    /// </summary>
    internal sealed class ClientCheckTimer : AutoCSer.Threading.TimerLink<ClientCheckTimer>
    {
        /// <summary>
        /// 链表首节点
        /// </summary>
        internal Client Head;
        /// <summary>
        /// 链表尾部
        /// </summary>
        internal Client End;
        /// <summary>
        /// 链表访问锁
        /// </summary>
        private volatile int queueLock;
        /// <summary>
        /// 客户端心跳检测定时
        /// </summary>
        /// <param name="seconds">超时秒数</param>
        private ClientCheckTimer(int seconds)
            : base(seconds)
        {
            set(this);
            OnTime.Set(Date.NowTime.OnTimeFlag.TcpSimpleClientCheckTimer);
        }
        /// <summary>
        /// 添加心跳检测
        /// </summary>
        /// <param name="value"></param>
        [MethodImpl(AutoCSer.MethodImpl.AggressiveInlining)]
        internal void Push(Client value)
        {
            value.CheckTimeoutSeconds = currentSeconds + seconds;
            while (System.Threading.Interlocked.CompareExchange(ref queueLock, 1, 0) != 0) AutoCSer.Threading.ThreadYield.Yield(AutoCSer.Threading.ThreadYield.Type.TimerLinkQueuePush);
            if (End == null)
            {
                End = Head = value;
                queueLock = 0;
            }
            else
            {
                End.CheckNext = value;
                value.CheckPrevious = End;
                End = value;
                queueLock = 0;
            }
        }
        /// <summary>
        /// 释放心跳检测
        /// </summary>
        /// <param name="value"></param>
        internal void Free(Client value)
        {
            while (System.Threading.Interlocked.CompareExchange(ref queueLock, 1, 0) != 0) AutoCSer.Threading.ThreadYield.Yield(AutoCSer.Threading.ThreadYield.Type.TimerLinkQueuePop);
            if (value == Head)
            {
                if ((Head = value.CheckNext) == null)
                {
                    End = null;
                    queueLock = 0;
                }
                else
                {
                    value.CheckNext = null;
                    Head.CheckPrevious = null;
                    queueLock = 0;
                }
            }
            else if (value == End)
            {
                End = value.CheckPrevious;
                value.CheckPrevious = null;
                End.CheckNext = null;
                queueLock = 0;
            }
            else
            {
                if (value.CheckNext != null) value.FreeCheck();
                queueLock = 0;
            }
        }
        /// <summary>
        /// 弹出节点
        /// </summary>
        /// <param name="currentSecond"></param>
        /// <returns></returns>
        [MethodImpl(AutoCSer.MethodImpl.AggressiveInlining)]
        private Client pop(long currentSecond)
        {
            while (System.Threading.Interlocked.CompareExchange(ref queueLock, 1, 0) != 0) AutoCSer.Threading.ThreadYield.Yield(AutoCSer.Threading.ThreadYield.Type.TimerLinkQueuePop);
            if (Head == null || Head.CheckTimeoutSeconds > currentSecond)
            {
                queueLock = 0;
                return null;
            }
            Client value = Head;
            if ((Head = Head.CheckNext) == null) End = null;
            else value.CheckNext = Head.CheckPrevious = null;
            queueLock = 0;
            return value;
        }
        /// <summary>
        /// 重置心跳检测
        /// </summary>
        /// <param name="value"></param>
        internal void Reset(Client value)
        {
            long newSeconds = currentSeconds + seconds;
            if (value.CheckTimeoutSeconds != newSeconds)
            {
                value.CheckTimeoutSeconds = newSeconds;
                while (System.Threading.Interlocked.CompareExchange(ref queueLock, 1, 0) != 0) AutoCSer.Threading.ThreadYield.Yield(AutoCSer.Threading.ThreadYield.Type.TimerLinkQueuePush);
                if (value != End)
                {
                    if (value.CheckNext == null)
                    {
                        if (End == null) Head = value;
                        else
                        {
                            End.CheckNext = value;
                            value.CheckPrevious = End;
                        }
                    }
                    else
                    {
                        if (value == Head)
                        {
                            (Head = value.CheckNext).CheckPrevious = null;
                            value.CheckNext = null;
                        }
                        else value.FreeCheckReset();
                        End.CheckNext = value;
                        value.CheckPrevious = End;
                    }
                    End = value;
                }
                queueLock = 0;
            }
        }
        /// <summary>
        /// 定时器触发
        /// </summary>
        [MethodImpl(AutoCSer.MethodImpl.AggressiveInlining)]
        internal void OnTimer()
        {
            ++currentSeconds;
            Client head = Head;
            if (head != null && head.CheckTimeoutSeconds <= currentSeconds && Interlocked.CompareExchange(ref isTimer, 1, 0) == 0)
            {
                do
                {
                    if ((head = pop(currentSeconds)) == null)
                    {
                        isTimer = 0;
                        return;
                    }
                    head.Check();
                }
                while (true);

            }
        }

        /// <summary>
        /// 获取客户端心跳检测定时
        /// </summary>
        /// <param name="seconds"></param>
        /// <returns></returns>
        internal static ClientCheckTimer Get(int seconds)
        {
            ++seconds;
            Monitor.Enter(timeoutLock);
            ClientCheckTimer timeout = TimeoutEnd;
            while (timeout != null)
            {
                if (timeout.seconds == seconds)
                {
                    ++timeout.count;
                    Monitor.Exit(timeoutLock);
                    return timeout;
                }
                timeout = timeout.DoubleLinkPrevious;
            }
            try
            {
                timeout = new ClientCheckTimer(seconds);
            }
            finally { Monitor.Exit(timeoutLock); }
            Date.NowTime.Flag |= Date.NowTime.OnTimeFlag.TcpSimpleClientCheckTimer;
            return timeout;
        }
    }
}
