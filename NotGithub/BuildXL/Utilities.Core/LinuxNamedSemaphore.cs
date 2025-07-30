// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Interop.Linux;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Wrapper for Linux pthread semaphores.
    /// </summary>
    public class LinuxNamedSemaphore : INamedSemaphore
    {
        /// <summary>
        /// Represents a wrapper for a semaphore handle.
        /// </summary>
        public class SemaphoreHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private readonly string m_name;

            /// <nodoc />
            public SemaphoreHandle(string name, IntPtr handle)
                : base(true)
            {
                m_name = name;
                SetHandle(handle);
            }

            /// <inheritdoc />
            protected override bool ReleaseHandle()
            {
                Ipc.SemClose(handle);
                Ipc.SemUnlink(m_name);
                return true;
            }
        }

        /// <inheritdoc/>
        public string Name => m_name;

        private readonly SemaphoreHandle m_semaphore;
        private readonly string m_name;

        /// <nodoc/>
        private LinuxNamedSemaphore(string name, SemaphoreHandle semaphore)
        {
            m_name = name;
            m_semaphore = semaphore;
        }

        /// <summary>
        /// Try to create a named pthread semaphore.
        /// </summary>
        /// <param name="name">Name must be of the form /name up to 251 characters.</param>
        /// <param name="initialValue">Initial value of the semaphore</param>
        private static Possible<Unit> ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] != '/')
            {
                return new Failure<ArgumentException>(new ArgumentException("Semaphore name must start with '/'"));
            }

            if (name.Count(c => c == '/') != 1)
            {
                return new Failure<ArgumentException>(new ArgumentException("Semaphore name must start with '/' and contain exactly one '/' character"));
            }

            if (name.Length >= Ipc.SemaphoreNameMaxLength)
            {
                return new Failure<ArgumentException>(new ArgumentException($"Semaphore name can only contain up to {Ipc.SemaphoreNameMaxLength} characters."));
            }

            return Unit.Void;
        }

        private static Possible<INamedSemaphore> CreateOrOpenInternal(string name, uint initialValue, bool createNew)
        {
            var validation = ValidateName(name);
            if (!validation.Succeeded)
            {
                return validation.Failure;
            }

            var error = Ipc.SemOpen(name, initialValue, out var semaphorePtr, errorIfExists: createNew);

            if (semaphorePtr == IntPtr.Zero || error != 0)
            {

                var failureMessage = createNew
                    ? (error == 17 ? $"Semaphore with name '{name}' already exists." : $"Failed to create a semaphore with name '{name}' and value {initialValue} with errno: {error}")
                    : $"Failed to create or open a semaphore with name '{name}' and value {initialValue} with errno: {error}";

                return new Failure<string>(failureMessage);
            }

            return new LinuxNamedSemaphore(name, new SemaphoreHandle(name, semaphorePtr));
        }

        public static Possible<INamedSemaphore> CreateNew(string name, uint initialValue)
        {
            try
            {
                return CreateOrOpenInternal(name, initialValue, createNew: true);
            }
            catch (Exception e)
            {
                return new Failure<Exception>(e);
            }
        }

        /// <summary>
        /// Try to create a named pthread semaphore, or open it if it already exists.
        /// </summary>
        /// <param name="name">Name must be of the form /name up to 251 characters.</param>
        /// <param name="initialValue">Initial value of the semaphore</param>
        public static Possible<INamedSemaphore> CreateOrOpen(string name, uint initialValue)
        {
            try
            {
                return CreateOrOpenInternal(name, initialValue, createNew: false);
            }
            catch (Exception e)
            {
                return new Failure<Exception>(e);
            }
        }

        /// <inheritdoc />
        public int Release()
        {
            int previousValue = GetValue();
            var handle = m_semaphore.DangerousGetHandle();
            int ret = Ipc.SemPost(handle);
            CheckReturnValue(ret);

            return previousValue;
        }

        /// <summary>
        /// Gets the current value of the semaphore
        /// </summary>
        private int GetValue()
        {
            var handle = m_semaphore.DangerousGetHandle();
            int ret = Ipc.SemGetValue(handle, out int value);
            CheckReturnValue(ret);

            return value;
        }

        /// <inheritdoc />
        public bool WaitOne(int timeoutMilliseconds)
        {
            // Timed wait currently not supported on Linux, provided timeout is ignored unless it's less than zero in which case we wait indefinitely.
            int ret = 0;
            var handle = m_semaphore.DangerousGetHandle();
            if (timeoutMilliseconds < 0)
            {
                ret = Ipc.SemWait(handle);
            }
            else
            {
                if (timeoutMilliseconds > 1)
                {
                    throw new NotSupportedException("Timed wait is not supported on Linux named semaphores.");
                }
                ret = Ipc.SemTryWait(handle);
            }

            CheckReturnValue(ret);

            return true;
        }

        /// <summary>
        /// Checks the return value and throws an exception with the errno if an operation failed.
        /// </summary>
        private void CheckReturnValue(int ret, [CallerMemberName] string op = "")
        {
            if (ret != 0)
            {
                throw new Exception($"{op} failed for semaphore '{Name}' with errno {ret}");
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_semaphore.Dispose();
        }
    }
}
