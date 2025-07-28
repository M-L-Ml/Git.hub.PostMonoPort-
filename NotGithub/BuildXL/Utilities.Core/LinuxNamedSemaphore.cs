// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Interop.Linux;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Wrapper for Linux pthread semaphores.
    /// </summary>
    public class LinuxNamedSemaphore : INamedSemaphore
    {
        /// <inheritdoc/>
        public string Name => m_name;

        private readonly IntPtr m_semaphore;
        private readonly string m_name;
        private bool m_disposed;

        /// <nodoc/>
        private LinuxNamedSemaphore(string name, IntPtr semaphore)
        {
            m_name = name;
            m_semaphore = semaphore;
        }

        /// <nodoc/>
        ~LinuxNamedSemaphore()
        {
            Dispose(false);
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

            var error = Ipc.SemOpen(name, initialValue, out var semaphore, errorIfExists: createNew);

            if (semaphore == IntPtr.Zero || error != 0)
            {

                var failureMessage = createNew
                    ? (error == 17 ? $"Semaphore with name '{name}' already exists." : $"Failed to create a semaphore with name '{name}' and value {initialValue} with errno: {error}")
                    : $"Failed to create or open a semaphore with name '{name}' and value {initialValue} with errno: {error}";

                return new Failure<string>(failureMessage);
            }

            return new LinuxNamedSemaphore(name, semaphore);
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
            int ret = Ipc.SemPost(m_semaphore);
            CheckReturnValue(ret);

            return previousValue;
        }

        /// <summary>
        /// Gets the current value of the semaphore
        /// </summary>
        private int GetValue()
        {
            int ret = Ipc.SemGetValue(m_semaphore, out int value);
            CheckReturnValue(ret);

            return value;
        }

        /// <inheritdoc />
        public bool WaitOne(int timeoutMilliseconds)
        {
            // Timed wait currently not supported on Linux, provided timeout is ignored unless it's less than zero in which case we wait indefinitely.
            int ret = 0;
            if (timeoutMilliseconds < 0)
            {
                ret = Ipc.SemWait(m_semaphore);
            }
            else
            {
                if (timeoutMilliseconds > 1)
                {
                    throw new NotSupportedException("Timed wait is not supported on Linux named semaphores.");
                }
                ret = Ipc.SemTryWait(m_semaphore);
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
            Dispose(true);
        }

        /// <nodoc/>
        public void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
                int ret = Ipc.SemClose(m_semaphore);
                ret = Ipc.SemUnlink(Name);

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }
        }
    }
}
