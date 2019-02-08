using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Photon.Hive;
using Photon.Hive.Plugin;
using MySql.Data;
using MySql.Data.MySqlClient;

namespace TestPlugin
{
    public class RaiseEventTestPlugin : PluginBase
    {
        private string connStr;
        private MySqlConnection conn;
        public string ServerString
        {
            get;
            private set;
        }
        public int CallsCount
        {
            get;
            private set;
        }
        public RaiseEventTestPlugin()
        {
            this.UseStrictMode = true;
            this.ServerString = "ServerMessage";
            this.CallsCount = 0;
            //this.PluginHost.LogDebug("Connection");
            // --- Connect to MySql.
            ConnectToMySQL();
        }
        public override string Name
        {
            get
            {
                return this.GetType().Name;
            }
        }
        public override void OnRaiseEvent(IRaiseEventCallInfo info)
        {
            try
            {
                base.OnRaiseEvent(info);
            }
            catch (Exception e)
            {
                this.PluginHost.BroadcastErrorInfoEvent(e.ToString(), info);
                return;
            }
            MySqlCommand cmd = new MySqlCommand("", conn);                                     //creates cmd to perform querys
            switch (info.Request.EvCode)
            {
                case 3:     //Game initialisation
                    {
                        string RecvdMessage = Encoding.Default.GetString((byte[])info.Request.Data);
                        string playerName = GetStringDataFromMessage(RecvdMessage, "PlayerName");
                        string playerPassword = GetStringDataFromMessage(RecvdMessage, "Password");

                        //Password authentication
                        string sql = "SELECT password FROM photon.users WHERE name='" + playerName + "'";   //String to find password
                        cmd.CommandText = sql;
                        string passwordCheck = RetrieveSingleDataFromMySQL(cmd/*, sql*/);
                        if (passwordCheck != "")
                        {
                            if (passwordCheck != playerPassword)
                            {
                                BroadcastPasswordAuthenticationResult(info.ActorNr, "Wrong Password");
                                return;
                            }
                            else
                            {
                                BroadcastPasswordAuthenticationResult(info.ActorNr, "Correct Password");
                            }
                        }
                        else
                        {
                            //string existingPassword = new MySqlCommand("SELECT password FROM photon.users WHERE name = '"+ playerName +"'", conn).ExecuteNonQuery().ToString();
                            sql = "INSERT INTO users(name, password, date_created, player_pos, pet_pos, inventory, friend_list) VALUES('" + playerName + "','" + playerPassword + "',NOW(), '50,3,47', '55,3,47', '', '')";
                            this.PluginHost.LogDebug(sql);
                            cmd.CommandText = sql;
                            cmd.ExecuteNonQuery();
                        }

                        //Retrieve and send spawn position
                        cmd.CommandText = "SELECT player_pos FROM photon.users WHERE name = '" + playerName + "'";
                        string player_spawnpos = RetrieveSingleDataFromMySQL(cmd);
                        cmd.CommandText = "SELECT pet_pos FROM photon.users WHERE name = '" + playerName + "'";
                        string pet_spawnpos = RetrieveSingleDataFromMySQL(cmd);
                        BroadcastPositionInformation(info.ActorNr, "Player:" + player_spawnpos + ";Pet:" + pet_spawnpos);

                        //Send Inventory info
                        cmd.CommandText = "SELECT inventory FROM photon.users WHERE name='" + playerName + "'";
                        string strInventory = RetrieveSingleDataFromMySQL(cmd);
                        BroadcastInventoryInformation(info.ActorNr, strInventory);

                        //Send Friend list
                        cmd.CommandText = "SELECT friend_list FROM photon.users WHERE name='" + playerName + "'";
                        string strFriends = RetrieveSingleDataFromMySQL(cmd);
                        BroadcastFriendsListInformation(info.ActorNr, strFriends);

                        //Cues player's GameManager to initiate game
                        BroadcastToOnePerson(info.ActorNr, 2, "Start game");

                        ++this.CallsCount;
                        int cnt = this.CallsCount;
                        string ReturnMessage = info.Nickname + " clicked the button. Now the count is " + cnt.ToString();
                        this.PluginHost.BroadcastEvent(target: ReciverGroup.All,
                                                        senderActor: 0,
                                                        targetGroup: 0,
                                                        data: new Dictionary<byte, object>() { { (byte)245, ReturnMessage } },
                                                        evCode: 1,
                                                        cacheOp: 0);
                    }
                    break;
                case 5:     //Save data
                    {
                        string RecvdMessage = Encoding.Default.GetString((byte[])info.Request.Data);
                        //Gets player's pos from string
                        string pos_player = GetStringDataFromMessage(RecvdMessage, "Pos_player");
                        //Gets pet's pos from string
                        string pos_pet = GetStringDataFromMessage(RecvdMessage, "Pos_pet");
                        //Gets player's inventory string from string
                        string inventory = GetStringDataFromMessage(RecvdMessage, "Inventory");
                        //Saves player's new position
                        ReplaceSingleDataInMySql(_cmd: cmd,
                                                _nameOfField: "player_pos",
                                                _nameOfAssociatedField: info.Nickname,
                                                _newValue: pos_player);
                        //Saves pet's new position
                        ReplaceSingleDataInMySql(_cmd: cmd,
                                                _nameOfField: "pet_pos",
                                                _nameOfAssociatedField: info.Nickname,
                                                _newValue: pos_pet);
                        //Saves player's new inventory
                        ReplaceSingleDataInMySql(_cmd: cmd,
                                                _nameOfField: "inventory",
                                                _nameOfAssociatedField: info.Nickname,
                                                _newValue: inventory);
                    }
                    break;
                case 7:     //Add new friend
                    {
                        string RecvdMessage = Encoding.Default.GetString((byte[])info.Request.Data);
                        string nameOfFriend = GetStringDataFromMessage(RecvdMessage, "FriendName");     //extract name of new friend
                        string after = AddFriendsInToMySql(cmd, info.Nickname, nameOfFriend);
                        if (after != "")                       //Check Name exist in database
                        {
                            BroadcastFriendsListInformation(info.ActorNr, after);                //Broadcast new friend list to owner (nameOfFriend overwritten in function to hold new friendlist string)
                        }

                    }
                    break;
                case 8:     //Friend list changed
                    {
                        string RecvdMessage = Encoding.Default.GetString((byte[])info.Request.Data);
                        var sMessage = RecvdMessage.Split("="[0]);
                        ReplaceSingleDataInMySql(cmd, info.Nickname, "friend_list", sMessage[1]);
                    }
                    break;
            }
        }

        private void BroadcastToOnePerson (int _senderActor, byte _evCode, string _message)
        {
            this.PluginHost.BroadcastEvent(recieverActors: new List<int>() { _senderActor },
                                senderActor: 0,
                                evCode: _evCode,
                                data: new Dictionary<byte, object>() { { (byte)245, _message } },
                                cacheOp: CacheOperations.DoNotCache);
        }
        private void BroadcastPasswordAuthenticationResult(int _senderActor, string _message)
        {
            BroadcastToOnePerson(_senderActor, 3, _message);
        }
        private void BroadcastPositionInformation(int _senderActor, string _message)
        {
            BroadcastToOnePerson(_senderActor, 4, _message);
        }
        private void BroadcastInventoryInformation(int _senderActor, string _message)
        {
            BroadcastToOnePerson(_senderActor, 5, _message);
        }
        private void BroadcastFriendsListInformation(int _senderActor, string _message)
        {
            BroadcastToOnePerson(_senderActor, 6, _message);
        }

        private string RetrieveSingleDataFromMySQL(MySqlCommand _cmd/*, string _request*/)
        {
            //_cmd.CommandText = _request;
            MySqlDataReader resultReader = _cmd.EndExecuteReader(_cmd.BeginExecuteReader());        //Executes query and acquire readeable result data
            if (resultReader.HasRows && resultReader.Read())   //If result contains something(in this case password), opens for reading and check if data is not null
            {
                string result = resultReader.GetString(0);
                resultReader.Close();
                return result;
            }
            resultReader.Close();
            return "";
        }
        private bool ReplaceSingleDataInMySql(MySqlCommand _cmd, string _nameOfAssociatedField, string _nameOfField, string _newValue)
        {
            _cmd.CommandText = "SELECT " + _nameOfField + " FROM photon.users WHERE name='" + _nameOfAssociatedField+"'";
            string _oldValue = RetrieveSingleDataFromMySQL(_cmd);
            if (_oldValue == "")
            {
                _cmd.CommandText = "UPDATE photon.users SET " + _nameOfField + "= '" + _newValue + "' WHERE name='" + _nameOfAssociatedField + "'";
            }
            else
            {
                _cmd.CommandText = "UPDATE photon.users SET " + _nameOfField + "= REPLACE(" + _nameOfField + ", '" + _oldValue + "', '" + _newValue + "') WHERE name='" + _nameOfAssociatedField + "'";
            }
            if (_cmd.ExecuteNonQuery() > 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private string AddFriendsInToMySql(MySqlCommand _cmd, string _nameOfAdder, string _nameOfFriend)
        {
            _cmd.CommandText = "SELECT name FROM photon.users WHERE name='" + _nameOfFriend + "'";
            if (RetrieveSingleDataFromMySQL(_cmd) == "")
                return "";
            //Name can be found in MySql
            _cmd.CommandText = "SELECT friend_list FROM photon.users WHERE name='" + _nameOfAdder + "'";
            string before = RetrieveSingleDataFromMySQL(_cmd);
            ReplaceSingleDataInMySql(_cmd, _nameOfAdder, "friend_list", before + _nameOfFriend + ";");
            return before + _nameOfFriend + ";";
        }
        private string GetStringDataFromMessage(string _dataSource, string _dataName)
        {
            var splitSource = _dataSource.Split(";"[0]);    //splits the string after each ';'
            foreach(string str in splitSource)              //Iterates each broken down string
            {
                var wantedContent = str.Split("="[0]);      //split the string at '=' sign 
                if(wantedContent[0] == _dataName)           //If the first half of the broken down string is the name of data I'm finding
                {
                    return wantedContent[1];                //Returns the substring from after '='
                }
            }
            return "";                                      //returns empty string if the wanted data is not found
        }

        public void ConnectToMySQL()
        {
            //Connect to MySQL
            connStr = "server=localhost;user=root;database=photon;port=3306;password=S3rver1234!";
            conn = new MySqlConnection(connStr);
            try
            {
                conn.Open();
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        public void DisconnectFromMySQL()
        {
            conn.Close();
        }
    }
}