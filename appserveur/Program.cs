using ServerMachin;
using MySql;
using MySql.Data.MySqlClient;
class Program
{
    static async Task Main(string[] args)
    {
        string conString = "Server=localhost;Port=3306;Database=coconitro;User ID=root;Password=admin;";
        try
        {
            
            using (MySqlConnection con = new MySqlConnection(conString))
            {
                con.Open();

                Console.Write("Enter nickname:  ");
                string pseudo = Console.ReadLine();
                Console.Write("Enter password:  ");
                string password = Console.ReadLine();

                string query = "SELECT COUNT(*) FROM users WHERE id = 1 AND BINARY pseudo = @pseudo AND BINARY password = @password";
                using (MySqlCommand cmd = new MySqlCommand(query, con))
                {
                    cmd.Parameters.AddWithValue("@pseudo", pseudo);
                    cmd.Parameters.AddWithValue("@password", password);

                    long count = (long)cmd.ExecuteScalar();
                    if (count == 1) 
                    {
                        Console.WriteLine("Welcome Admin.");
                        var server = new Server();
                        await server.StartAsync(5000);
                    }
                    else
                    {
                        Console.WriteLine("Wrong password or nickname, server shutdown.");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }
}