
using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.Collections.Generic;
using System.Linq;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;
    const int MaxNumberOfClientConnections = 1000;

    private Dictionary<NetworkConnection, string> connectedUsers = new Dictionary<NetworkConnection, string>();
    private Dictionary<string, List<NetworkConnection>> gameRooms = new Dictionary<string, List<NetworkConnection>>();
    private Dictionary<string, TicTacToe> gameRoomsState = new Dictionary<string, TicTacToe>();

    [System.Serializable]
    public class User
    {
        public string username;
        public string password;

        public User(string username, string password)
        {
            this.username = username;
            this.password = password;
        }
    }

    private List<User> registeredUsers = new List<User>();

    void Start()
    {
        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndpoint endpoint = NetworkEndpoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);
    }

    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        #region Check Input and Send Msg

        if (Input.GetKeyDown(KeyCode.A))
        {
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient("Hello client's world, sincerely your network server", networkConnections[i]);
            }
        }

        #endregion

        networkDriver.ScheduleUpdate().Complete();

        #region Remove Unused Connections

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        #endregion

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
        }

        #endregion

        #region Manage Network Events

        DataStreamReader streamReader;
        NetworkPipeline pipelineUsedToSendEvent;
        NetworkEvent.Type networkEventType;

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
                continue;

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader, out pipelineUsedToSendEvent))
            {
                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        ProcessReceivedMsg(msg, networkConnections[i]);
                        buffer.Dispose();
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Client has disconnected from server");
                        if (connectedUsers.ContainsKey(networkConnections[i]))
                        {
                            connectedUsers.Remove(networkConnections[i]);
                        }
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }

        #endregion
    }

    private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        networkConnections.Add(connection);
        return true;
    }

    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    private void HandleLogin(string username, string password, NetworkConnection connection)
    {
        string storedPassword = PlayerPrefs.GetString(username, null);

        if (storedPassword != null && storedPassword == password)
        {
            SendMessageToClient("LoginSuccess", connection);
            connectedUsers[connection] = username;
            Debug.Log($"User {username} logged in successfully.");
        }
        else
        {
            SendMessageToClient("LoginFailed: Invalid username or password.", connection);
            Debug.Log($"Login failed for user {username}.");
        }
    }

    private void HandleCreateAccount(string username, string password, NetworkConnection connection)
    {
        if (PlayerPrefs.HasKey(username))
        {
            SendMessageToClient("AccountCreationFailed: Username already exists.", connection);
            Debug.Log($"Account creation failed for {username}: Username already exists.");
        }
        else
        {
            PlayerPrefs.SetString(username, password);
            PlayerPrefs.Save();
            SendMessageToClient("AccountCreated", connection);
            connectedUsers[connection] = username;
            Debug.Log($"Account created for user {username}.");
        }
    }

    private void ProcessReceivedMsg(string msg, NetworkConnection connection)
    {
        Debug.Log("Msg received = " + msg);

        if (msg.StartsWith("Login:"))
        {
            string[] parts = msg.Split(':');
            if (parts.Length == 3)
            {
                string username = parts[1];
                string password = parts[2];
                HandleLogin(username, password, connection);
            }
        }
        else if (msg.StartsWith("CreateAccount:"))
        {
            string[] parts = msg.Split(':');
            if (parts.Length == 3)
            {
                string username = parts[1];
                string password = parts[2];
                HandleCreateAccount(username, password, connection);
            }
        }
        else if (msg.StartsWith("CreateRoom:"))
        {
            string roomName = msg.Split(':')[1];
            HandleCreateRoom(roomName, connection);
        }
        else if (msg.StartsWith("JoinRoom:"))
        {
            string roomName = msg.Split(':')[1];
            HandleJoinRoom(roomName, connection);
        }
        else if (msg.StartsWith("CheckRoom:"))
        {
            string roomName = msg.Split(':')[1];
            HandleCheckRoom(roomName, connection);
        } else if (msg.StartsWith("LeaveRoom:"))
        {
            string roomName = msg.Split(':')[1];
            HandleLeaveRoom(roomName, connection);
        }
        else if (msg.StartsWith("PlayerMessage:"))
        {
            string message = msg.Split(':')[1];
            HandlePlayerMessage(message, connection);
        }
        else if (msg.StartsWith("PlayerMove:"))
        {
            string[] parts = msg.Split(':');
            if (parts.Length == 3)
            {
                string roomName = parts[1];
                int position = int.Parse(parts[2]);
                HandlePlayerMove(roomName, position, connection);
            }
        }
    }

    private void HandleCheckRoom(string roomName, NetworkConnection connection)
    {
        if (gameRooms.ContainsKey(roomName))
        {
            SendMessageToClient("RoomExists:" + roomName, connection);
            Debug.Log($"Room {roomName} exists. Notifying client.");
        }
        else
        {
            SendMessageToClient("RoomDoesNotExist:" + roomName, connection);
            Debug.Log($"Room {roomName} does not exist. Notifying client.");
        }
    }

    private void HandleCreateRoom(string roomName, NetworkConnection connection)
    {
        if (!gameRooms.ContainsKey(roomName))
        {
            gameRooms[roomName] = new List<NetworkConnection> { connection };
            SendMessageToClient("RoomCreated:" + roomName, connection);
            Debug.Log($"Player {connectedUsers[connection]} created room: {roomName}");
        }
        else
        {
            SendMessageToClient("RoomAlreadyExists:" + roomName, connection);
            Debug.Log($"Room {roomName} already exists.");
        }
    }

    private void HandleJoinRoom(string roomName, NetworkConnection connection)
    {
        if (gameRooms.ContainsKey(roomName))
        {
            var room = gameRooms[roomName];

            if (room.Count < 2)
            {
                room.Add(connection);
                SendMessageToClient("JoinedRoom:" + roomName, connection);

                if (room.Count == 2)
                {
                    StartGame(roomName);
                }
            }
            else
            {
                room.Add(connection);
                SendMessageToClient("SpectatorAssigned:" + roomName, connection);

                if (gameRoomsState.ContainsKey(roomName))
                {
                    var game = gameRoomsState[roomName];
                    string player1Name = connectedUsers[room[0]];
                    string player2Name = connectedUsers[room[1]];
                    UpdateClientsWithBoardState(roomName, game, player1Name, player2Name);
                }
            }
        }
        else
        {
            SendMessageToClient("RoomDoesNotExist:" + roomName, connection);
        }
    }

    private void StartGame(string roomName)
    {
        var connections = gameRooms[roomName];
        gameRoomsState[roomName] = new TicTacToe();

        string player1Name = connectedUsers[connections[0]];
        string player2Name = connectedUsers[connections[1]];

        connectedUsers[connections[0]] = "Player1";
        connectedUsers[connections[1]] = "Player2";

        SendMessageToClient($"GameStarted:{roomName}:true", connections[0]);
        SendMessageToClient($"GameStarted:{roomName}:false", connections[1]);

        for (int i = 2; i < connections.Count; i++)
        {
            SendMessageToClient($"GameStarted:{roomName}:false", connections[i]);
        }

        UpdateClientsWithBoardState(roomName, gameRoomsState[roomName], player1Name, player2Name);
    }


    private void HandleLeaveRoom(string roomName, NetworkConnection connection)
    {
        if (gameRooms.ContainsKey(roomName))
        {
            var connections = gameRooms[roomName];
            if (connections.Contains(connection))
            {
                connections.Remove(connection);

                foreach (var conn in connections)
                {
                    SendMessageToClient("PlayerLeft:" + connectedUsers[connection], conn);
                }

                Debug.Log($"Player {connectedUsers[connection]} has left room: {roomName}");

                if (connections.Count == 0)
                {
                    gameRooms.Remove(roomName);
                    if (gameRoomsState.ContainsKey(roomName))
                    {
                        gameRoomsState.Remove(roomName);
                    }
                    Debug.Log($"Room {roomName} has been removed as it is empty.");
                }
            }
        }
    }

    private void HandlePlayerMessage(string message, NetworkConnection senderConnection)
    {
        string roomName = FindPlayerRoom(senderConnection);
        if (roomName != null)
        {
            var connections = gameRooms[roomName];
            foreach (var connection in connections)
            {
                if (connection != senderConnection)
                {
                    SendMessageToClient($"OpponentMessage:{message}", connection);
                }
            }
        }
    }

    private string FindPlayerRoom(NetworkConnection playerConnection)
    {
        foreach (var room in gameRooms)
        {
            if (room.Value.Contains(playerConnection))
            {
                return room.Key;
            }
        }
        return null;
    }

    private void HandlePlayerMove(string roomName, int position, NetworkConnection connection)
    {
        if (!gameRooms.ContainsKey(roomName) || !gameRoomsState.ContainsKey(roomName))
        {
            SendMessageToClient("Error: Game not initialized", connection);
            return;
        }

        var game = gameRoomsState[roomName];
        var connections = gameRooms[roomName];

        string player1Name = connectedUsers[connections[0]];
        string player2Name = connectedUsers[connections[1]];

        if ((game.GetCurrentPlayer() == TicTacToe.Player.X && connectedUsers[connection] == "Player1") ||
            (game.GetCurrentPlayer() == TicTacToe.Player.O && connectedUsers[connection] == "Player2"))
        {
            if (game.MakeMove(position))
            {
                UpdateClientsWithBoardState(roomName, game, player1Name, player2Name);

                NetworkConnection currentPlayer = connection;
                NetworkConnection otherPlayer = connections.Find(c => c != connection);

                SendMessageToClient("OpponentTurn", currentPlayer);
                SendMessageToClient("YourTurn", otherPlayer);

                var winner = game.GetWinner();
                if (winner != TicTacToe.Player.None)
                {
                    EndGame(roomName, winner, player1Name, player2Name);
                }
                else if (game.IsBoardFull())
                {
                    EndGame(roomName, TicTacToe.Player.None, player1Name, player2Name);
                }
            }
            else
            {
                SendMessageToClient("Invalid move. Try again.", connection);
            }
        }
        else
        {
            SendMessageToClient("It's not your turn!", connection);
        }
    }

    private void UpdateClientsWithBoardState(string roomName, TicTacToe game, string player1Name, string player2Name)
    {
        if (!gameRooms.ContainsKey(roomName)) return;

        var boardState = game.GetBoardState();
        var message = $"BoardState:{string.Join(",", boardState.Select(p => p.ToString()))}:{player1Name}:{player2Name}";

        foreach (var conn in gameRooms[roomName])
        {
            SendMessageToClient(message, conn);
        }
    }

    private void EndGame(string roomName, TicTacToe.Player winner, string player1Name, string player2Name)
    {
        string winnerName = winner == TicTacToe.Player.X ? player1Name : player2Name;
        string winnerMessage = winner == TicTacToe.Player.None ? "It's a draw!" : $"{winnerName} ({winner}) wins!";

        foreach (var conn in gameRooms[roomName])
        {
            SendMessageToClient("GameOver:" + winnerMessage, conn);
        }

        gameRooms.Remove(roomName);
        gameRoomsState.Remove(roomName);
    }

    public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    {
        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);
        DataStreamWriter streamWriter;

        networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }
}
