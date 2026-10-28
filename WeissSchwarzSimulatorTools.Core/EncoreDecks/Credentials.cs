// The Encore Decks session, kept in the operating system's credential store: the Secret Service
// (GNOME Keyring, KWallet) on Linux, the Credential Manager on Windows. They encrypt it at rest
// and tie it to the user's login. It is never written to the settings file, and there is no
// plaintext fallback: without a credential store, the account cannot be linked.
//
// Every call may block (the store can ask to be unlocked): run them off the UI thread.

using System.Runtime.InteropServices;
using System.Text;

namespace WeissSchwarzSimulatorTools.EncoreDecks;

public static partial class Credentials
{
    // The program's former name, kept so that sessions saved by earlier versions still load.
    private const string Service = "simu_deck_parser.encoredecks";

    private const string NoStore =
        "no secure credential store is available (Secret Service / KWallet on Linux, Credential Manager on Windows)";

    public static void SaveSession(string account, string session)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsStore.Save(account, session);
            else if (OperatingSystem.IsLinux()) SecretService.Save(account, session);
            else throw new UserException(NoStore);
        }
        catch (Exception e) when (e is not UserException)
        {
            throw Errors.Context("could not save the session in the system credential store", e);
        }
    }

    /// <summary>Null when the store has no session for this account.</summary>
    public static string? LoadSession(string account)
    {
        try
        {
            if (OperatingSystem.IsWindows()) return WindowsStore.Load(account);
            if (OperatingSystem.IsLinux()) return SecretService.Load(account);
            throw new UserException(NoStore);
        }
        catch (Exception e) when (e is not UserException)
        {
            throw Errors.Context("could not read the session from the system credential store", e);
        }
    }

    /// <summary>Removing a session that is not there is not an error.</summary>
    public static void DeleteSession(string account)
    {
        try
        {
            if (OperatingSystem.IsWindows()) WindowsStore.Delete(account);
            else if (OperatingSystem.IsLinux()) SecretService.Delete(account);
            else throw new UserException(NoStore);
        }
        catch (Exception e) when (e is not UserException)
        {
            throw Errors.Context("could not remove the session from the system credential store", e);
        }
    }

    /// <summary>The Windows Credential Manager, through advapi32 (generic credentials).</summary>
    private static partial class WindowsStore
    {
        private const int CredTypeGeneric = 1;
        private const int CredPersistLocalMachine = 2;
        private const int ErrorNotFound = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public int Flags;
            public int Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }

        [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CredWrite(ref Credential credential, int flags);

        [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CredRead(string target, int type, int flags, out IntPtr credential);

        [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CredDelete(string target, int type, int flags);

        [LibraryImport("advapi32.dll")]
        private static partial void CredFree(IntPtr buffer);

        /// <summary>The same target name as the Rust keyring crate: "account.service".</summary>
        private static string Target(string account) => $"{account}.{Service}";

        public static void Save(string account, string session)
        {
            var blob = Encoding.Unicode.GetBytes(session);
            var target = Marshal.StringToHGlobalUni(Target(account));
            var user = Marshal.StringToHGlobalUni(account);
            var blobPtr = Marshal.AllocHGlobal(blob.Length);
            try
            {
                Marshal.Copy(blob, 0, blobPtr, blob.Length);
                var credential = new Credential
                {
                    Type = CredTypeGeneric,
                    TargetName = target,
                    CredentialBlobSize = blob.Length,
                    CredentialBlob = blobPtr,
                    Persist = CredPersistLocalMachine,
                    UserName = user,
                };
                if (!CredWrite(ref credential, 0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
            }
            finally
            {
                Array.Clear(blob);
                Marshal.Copy(new byte[blob.Length], 0, blobPtr, blob.Length);
                Marshal.FreeHGlobal(blobPtr);
                Marshal.FreeHGlobal(target);
                Marshal.FreeHGlobal(user);
            }
        }

        public static string? Load(string account)
        {
            if (!CredRead(Target(account), CredTypeGeneric, 0, out var pointer))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error == ErrorNotFound) return null;
                throw new System.ComponentModel.Win32Exception(error);
            }
            try
            {
                var credential = Marshal.PtrToStructure<Credential>(pointer);
                return Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
            }
            finally
            {
                CredFree(pointer);
            }
        }

        public static void Delete(string account)
        {
            if (!CredDelete(Target(account), CredTypeGeneric, 0))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorNotFound)
                    throw new System.ComponentModel.Win32Exception(error);
            }
        }
    }

    /// <summary>
    /// The freedesktop Secret Service (GNOME Keyring, KWallet), through libsecret. Items carry the
    /// attributes service and username, like the ones the Rust keyring crate wrote.
    /// </summary>
    private static partial class SecretService
    {
        private const string LibSecret = "libsecret-1.so.0";
        private const string LibGlib = "libglib-2.0.so.0";
        /// <summary>SECRET_SCHEMA_DONT_MATCH_NAME: find items stored under another schema name too.</summary>
        private const int DontMatchName = 1 << 1;

        [LibraryImport(LibGlib)]
        private static partial IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

        [LibraryImport(LibGlib)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static partial int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

        [LibraryImport(LibGlib)]
        private static partial void g_hash_table_unref(IntPtr table);

        [LibraryImport(LibGlib)]
        private static partial void g_error_free(IntPtr error);

        [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr secret_schema_newv(string name, int flags, IntPtr attributeTypes);

        [LibraryImport(LibSecret)]
        private static partial void secret_schema_unref(IntPtr schema);

        [LibraryImport(LibSecret, StringMarshalling = StringMarshalling.Utf8)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static partial int secret_password_storev_sync(IntPtr schema, IntPtr attributes, string? collection,
            string label, IntPtr password, IntPtr cancellable, out IntPtr error);

        [LibraryImport(LibSecret)]
        private static partial IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

        [LibraryImport(LibSecret)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static partial int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

        [LibraryImport(LibSecret)]
        private static partial void secret_password_free(IntPtr password);

        /// <summary>A GHashTable of UTF-8 strings, and the native memory it points to.</summary>
        private sealed class StringTable : IDisposable
        {
            private readonly List<IntPtr> _strings = [];
            public IntPtr Handle { get; }

            public StringTable()
            {
                var glib = NativeLibrary.Load(LibGlib);
                Handle = g_hash_table_new(NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"));
            }

            /// <summary>Inserts key -> value; a null value is stored as the NULL pointer.</summary>
            public void Insert(string key, string? value)
            {
                var k = Marshal.StringToCoTaskMemUTF8(key);
                _strings.Add(k);
                var v = IntPtr.Zero;
                if (value is not null)
                {
                    v = Marshal.StringToCoTaskMemUTF8(value);
                    _strings.Add(v);
                }
                g_hash_table_insert(Handle, k, v);
            }

            public void Dispose()
            {
                g_hash_table_unref(Handle);
                foreach (var s in _strings)
                    Marshal.FreeCoTaskMem(s);
            }
        }

        private static T WithSchema<T>(string account, Func<IntPtr, IntPtr, T> action)
        {
            IntPtr schema;
            using (var types = new StringTable())
            {
                // Both attributes are strings: SECRET_SCHEMA_ATTRIBUTE_STRING is 0, a NULL value.
                types.Insert("service", null);
                types.Insert("username", null);
                try
                {
                    schema = secret_schema_newv(Service, DontMatchName, types.Handle);
                }
                catch (DllNotFoundException e)
                {
                    throw Errors.Context(NoStore, e);
                }
            }
            try
            {
                using var attributes = new StringTable();
                attributes.Insert("service", Service);
                attributes.Insert("username", account);
                return action(schema, attributes.Handle);
            }
            finally
            {
                secret_schema_unref(schema);
            }
        }

        private static void ThrowIfError(IntPtr error)
        {
            if (error == IntPtr.Zero) return;
            // struct GError { GQuark domain; gint code; gchar *message; }
            var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "unknown error";
            g_error_free(error);
            throw new UserException(message);
        }

        public static void Save(string account, string session) =>
            WithSchema(account, (schema, attributes) =>
            {
                var password = Marshal.StringToCoTaskMemUTF8(session);
                try
                {
                    secret_password_storev_sync(schema, attributes, "default", $"WeissSchwarz Simulator Tools: Encore Decks session of {account}",
                        password, IntPtr.Zero, out var error);
                    ThrowIfError(error);
                    return 0;
                }
                finally
                {
                    Marshal.ZeroFreeCoTaskMemUTF8(password);
                }
            });

        public static string? Load(string account) =>
            WithSchema(account, (schema, attributes) =>
            {
                var password = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, out var error);
                ThrowIfError(error);
                if (password == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringUTF8(password);
                }
                finally
                {
                    // Wipes the secret before freeing it.
                    secret_password_free(password);
                }
            });

        public static void Delete(string account) =>
            WithSchema(account, (schema, attributes) =>
            {
                secret_password_clearv_sync(schema, attributes, IntPtr.Zero, out var error);
                ThrowIfError(error);
                return 0;
            });
    }
}
