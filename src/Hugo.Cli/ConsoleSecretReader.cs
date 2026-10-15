namespace Hugo.Cli
{
    using System;
    using System.Text;

    /// <summary>
    /// Reads a secret from the terminal without echoing it. Not thread-safe with respect to other console readers.
    /// </summary>
    public static class ConsoleSecretReader
    {
        /// <summary>
        /// Write the prompt to stderr and read keys until Enter, without echo. Backspace removes the last character.
        /// When the terminal cannot be read key by key, the secret is read as a plain line.
        /// </summary>
        /// <param name="prompt">Prompt text.</param>
        /// <returns>The secret, or null at end of input.</returns>
        public static string? Read(string prompt)
        {
            Console.Error.Write(prompt);
            StringBuilder secret = new StringBuilder();
            try
            {
                while (true)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Enter) break;
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (secret.Length > 0) secret.Length--;
                        continue;
                    }
                    if (!Char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
                }
            }
            catch (InvalidOperationException)
            {
                Console.Error.WriteLine();
                return Console.In.ReadLine();
            }
            Console.Error.WriteLine();
            return secret.ToString();
        }
    }
}
