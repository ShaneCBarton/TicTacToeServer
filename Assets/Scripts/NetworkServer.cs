using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using System.Text;
using System.Collections.Generic;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;

    const int MaxNumberOfClientConnections = 1000;

    private Dictionary<NetworkConnection, string> connectedUsers = new Dictionary<NetworkConnection, string>();

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

            int connectionIndex = networkConnections.IndexOf(connection);
            Debug.Log($"User connected: {username}, Connection Index: {connectionIndex}");

        }
        else
        {
            SendMessageToClient("LoginFailed: Invalid username or password.", connection);
        }
    }

    private void HandleCreateAccount(string username, string password, NetworkConnection connection)
    {
        if (PlayerPrefs.HasKey(username))
        {
            SendMessageToClient("AccountCreationFailed: Username already exists.", connection);
        }
        else
        {
            PlayerPrefs.SetString(username, password);
            PlayerPrefs.Save();
            SendMessageToClient("AccountCreated", connection);
            connectedUsers[connection] = username;

            int connectionIndex = networkConnections.IndexOf(connection);
            Debug.Log($"User created account: {username}, Connection Index: {connectionIndex}");

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
