// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// A single component of a file path.
    /// </summary>
    /// <remarks>
    /// This type represents a single file name or directory name.
    /// </remarks>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct PathAtom //: IEquatable<PathAtom>, IPathSegment
    {
        /// <summary>
        /// Invalid atom for uninitialized fields.
        /// </summary>
        public static readonly PathAtom Invalid = default(PathAtom);

        private static readonly BitArray s_invalidPathAtomChars = new BitArray(65536); // one bit per char value

        [SuppressMessage("Microsoft.Usage", "CA2207:InitializeValueTypeStaticFieldsInline")]
        static PathAtom()
        {
            // set a bit for each invalid character value
            foreach (char ch in Path.GetInvalidFileNameChars())
            {
                s_invalidPathAtomChars.Set(ch, true);
            }

            // also explicitly disallow control characters and other weird things just to help maintain sanity
            for (int i = 0; i < 65536; i++)
            {
                var ch = unchecked((char)i);
                if (char.IsControl(ch))
                {
                    s_invalidPathAtomChars.Set(ch, true);
                }
            }
        }

        internal PathAtom(StringId value)
        {
            Contract.RequiresDebug(value.IsValid);
            StringId = value;
        }

        /// <summary>
        /// Unsafe factory method that constructs <see cref="PathAtom"/> instance from the underlying string id.
        /// </summary>
        public static PathAtom UnsafeCreateFrom(StringId value)
        {
            return new PathAtom(value);
        }

        /// <summary>
        /// Validate whether a string is a valid path atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        public static bool Validate<T>(T prospectiveAtom)
            where T : struct, ICharSpan<T>
        {
            ParseResult parseResult = Validate(prospectiveAtom, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Validate whether a string is a valid path atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        public static ParseResult Validate<T>(T prospectiveAtom, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            if (prospectiveAtom.Length == 0)
            {
                // can't be empty
                characterWithError = 0;
                return ParseResult.FailureDueToEmptyValue;
            }

            if (prospectiveAtom.CheckIfOnlyContainsValidPathAtomChars(out characterWithError))
            {
                // NOTE: In theory, we should prevent path atoms that use the well-known no-no strings
                //       from Windows such as AUX, COM1, LPN1. We don't do that though, it's not worth the
                //       cycles.
                return ParseResult.Success;
            }

            return ParseResult.FailureDueToInvalidCharacter;
        }

 ////del

        /// <nodoc/>
        [ExcludeFromCodeCoverage]
        public string ToDebuggerDisplay() => StringId.ToDebuggerDisplay();

#pragma warning disable 809

        /// <summary>
        /// Not available for PathAtom, throws an exception
        /// </summary>
        [Obsolete("Not suitable for PathAtom")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(PathAtom).
        /// </summary>
        public bool IsValid => StringId.IsValid;

        /// <summary>
        /// Returns the string identifier for this atom.
        /// </summary>
        public StringId StringId { get; }

        /// <summary>
        /// Explains parsing errors.
        /// </summary>
        public enum ParseResult
        {
            /// <summary>
            /// Successfully parsed
            /// </summary>
            Success = 0,

            /// <summary>
            /// Invalid character.
            /// </summary>
            FailureDueToInvalidCharacter,

            /// <summary>
            /// Empty PathAtom is not allowed to be empty.
            /// </summary>
            FailureDueToEmptyValue,
        }
    }
}
