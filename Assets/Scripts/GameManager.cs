﻿using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;
using System;

public class GameManager : MonoBehaviour
{
    public GameObject CanvasSelection;
    public Button SubmitButton;
    private static GameManager _instance;
    public enum State
    {
        PlacingShips,
        Playing
    }
    public static State CurrentState;
    public static int CurrentShipId = -1;
    public static GameObject CurrentInstanciatedChip;
    public static int LastRotation;
    public static int NShipsToPlace = 1;
    public static Dictionary<int, ShipData> ShipDatas = new Dictionary<int, ShipData>();
    public static Dictionary<PlayerCell.CellType, Material> CellMaterials = new Dictionary<PlayerCell.CellType, Material>();
    private bool _boarded = false;
    public static bool PlacingShips => CurrentState == State.PlacingShips;
    public static bool IsShipSelected => CurrentShipId != -1;


    private ShipPlacement _shipPlacement;
    
    private int _turn = -1;
    private Player[] _players;

    private Player Me => _players[MyID];
    private Player CurrentPlayer => _players[_turn];

    public static int MyID { get; private set; }
    public static int PlayersNumber => _instance._players.Length;
    public static bool MyTurn => _instance._turn == MyID;

    [Serializable]
    public struct ShipData
    {
        public GameObject prefab;
        public Button button;
        public int id;
        public int length;
    }
    [Serializable]
    private struct CellMaterial
    {
        public PlayerCell.CellType cellType;
        public Material material;
    }
    [SerializeField] private ShipData[] _shipsDatas;
    [SerializeField] private CellMaterial[] _cellsMaterials;
    private bool _displayShipMenu = false;
    private Player _player;

    private int _iRemove;
    private int _jRemove;

    [SerializeField]
    private Missile _missilePrefab;
    [SerializeField] private Transform _exampleFiringPoint;
    [SerializeField] private Transform _examplePlayer;
    [SerializeField] private Vector3 _localSpawnPosition;
    //-112 -45


    //temporaire je ne sais pas comment faire avec les property : Bryan
    public static string GetPlayerNameByID(int ID)
    {
        return _instance._players[ID].nickName;
    }

    private void Awake()
    {
        _instance = this;
        foreach (ShipData shipData in _shipsDatas)
        {
            ShipDatas.Add(shipData.id, shipData);
        }
        foreach (CellMaterial cellMat in _cellsMaterials)
        {
            CellMaterials.Add(cellMat.cellType, cellMat.material);
        }

        _localSpawnPosition = _exampleFiringPoint.position - _examplePlayer.position;
    }

    private void Update()
    {
        if (PlacingShips)
        {
            SubmitButton.interactable = _boarded ? false : NShipsToPlace == 0;
        }
    }
    public void Boarded()
    {
        _boarded = true;
        ClientManager.Boarded();
    }

    void OnGUI()
    {
        if (_displayShipMenu && !_boarded)
        {
            Vector2 position = Camera.main.WorldToScreenPoint(CurrentInstanciatedChip.transform.position);
            position.y = Screen.height - position.y;
            GUILayout.BeginArea(new Rect(position.x, position.y, 300, 400), GUI.skin.box);

            GUIStyle labelStyle = new GUIStyle("Label") { fontSize = 32 };
            GUIStyle buttonStyle = new GUIStyle("Button") { fontSize = 32 };

            GUILayout.Label("Remove this ship ?", labelStyle);

            if (GUILayout.Button("Remove it", buttonStyle))
            {
                _player.RemoveShip(_iRemove, _jRemove);
                ClientManager.RemoveShip(_iRemove, _jRemove);

                ++NShipsToPlace;
                ShipDatas[CurrentInstanciatedChip.GetComponentInChildren<Ship>().id].button.interactable = true;
                Destroy(CurrentInstanciatedChip);
                CurrentShipId = -1;
                CurrentInstanciatedChip = null;

                _displayShipMenu = false;
            }

            if (GUILayout.Button("Cancel", buttonStyle))
            {
                CurrentInstanciatedChip = null;

                _displayShipMenu = false;
            }
            GUILayout.EndArea();
        }
    }

    public static void OnCellClicked(PlayerCell cell)
    {
        Player player = cell.transform.parent.GetComponent<Player>();
        if (_instance._displayShipMenu)
            return;
        if (MyTurn && !player.you)
        {
            ClientManager.Shoot(player.id, cell.position.x, cell.position.y);
        }
        else if (PlacingShips)
        {
            if (IsShipSelected)
            {
                if (PlaceChip(cell.position, player))
                {
                    CurrentInstanciatedChip = null;
                    CurrentShipId = -1;
                }
            }
            else
            {
                if (cell.ship != null)
                {
                    CurrentInstanciatedChip = cell.ship;
                    _instance._iRemove = cell.position.x;
                    _instance._jRemove = cell.position.y;
                    _instance._displayShipMenu = true;
                }
            }
        }
        Debug.Log(cell.position.ToString() + cell.type.ToString());
    }

    public static void Board(int id, int count)
    {
        _instance._players = Map.CreatePlayers(id, count);

        UIManager.ShowMenu(UIManager.Menu.Board);
    }

    public static void KillPlayer(int id)
    {
        _instance._players[id].dead = true;
    }

    public static void Play()
    {
        _instance._turn = 0;

        CurrentState = State.Playing;

        UIManager.SetTurn(ClientManager.GetName(_instance._turn));
    }

    public static void Shoot(int id, int x, int y, Action onTargetReach = null)
    {
        Player target = _instance._players[id];
        Transform player = _instance.CurrentPlayer.transform;

        Vector3 from = player.position + _instance._localSpawnPosition;
        Vector3 to = target.GetWorldPosition(x, y);

        ClientManager.Wait = true;

        Vector3 worldSpawnPosition = player.forward * _instance._localSpawnPosition.z + player.right * _instance._localSpawnPosition.x;

        Missile missile = Instantiate(_instance._missilePrefab);
        missile.SetCallbacks(
            onTargetReach,
            delegate () {
                UIManager.SetTurn(_instance.CurrentPlayer.nickName);
            }
        );

        missile.Shoot(from, to, onTargetReach != null);

        UIManager.ShowShoot(_instance.CurrentPlayer.nickName, target.nickName);

        target.SetCellType(x, y, PlayerCell.CellType.ShipHit);

        do
        {
            _instance._turn = (_instance._turn + 1) % _instance._players.Length;
        } while (_instance.CurrentPlayer.dead);


    }
    public static void Shoot(int id, int x, int y, int shipId, int shipX, int shipY, int shipDir)//destroyed == true
    {
        Player target = _instance._players[id];
        Shoot(id, x, y, true, delegate () {
            Transform ship;
            if (!target.you)
            {
                ship = Instantiate(ShipDatas[shipId].prefab, target.GetWorldPosition(shipX, shipY),
                    Quaternion.identity, target.transform).transform;
            }
            else
                ship = target.GetShip(x, y).transform;
            ship.localRotation *= Quaternion.Euler(Vector3.right * 1000f);
        });
       
    }

    public static void Shoot(int id, int x, int y, bool touched)
    {
        Vector3 startPosition = _instance.CurrentPlayer.transform.position; //Get laucnh position
        Vector3 endPosition = _instance._players[id].transform.position;
        Missile missile = Instantiate(_instance._missilePrefab);
        missile.Shoot(startPosition, endPosition, touched);
    }

    public static void RotateChip()
    {
        if (CurrentShipId == -1)
            return;
        LastRotation += 90;
        LastRotation %= 360;
        CurrentInstanciatedChip.transform.Rotate(Vector3.up * 90);
    }
    public void ChangeCurrentChip(int id)
    {
        if (IsShipSelected)
        {
            Destroy(CurrentInstanciatedChip);
            ShipDatas[CurrentShipId].button.interactable = true;
        }
        ShipDatas[id].button.interactable = false;
        CurrentShipId = id;
    }
    public static void PrevisualizeShipOnCell(Transform cellTransform)
    {
        GameObject newChip = ShipDatas[CurrentShipId].prefab;
        CurrentInstanciatedChip = Instantiate(newChip, cellTransform.position, cellTransform.rotation);
        CurrentInstanciatedChip.transform.SetParent(cellTransform.parent);
        CurrentInstanciatedChip.transform.Rotate(cellTransform.up * LastRotation);
    }
    public static bool PlaceChip(Vector2Int cellPosition, Player player)
    {
        int dir = LastRotation / 90;
        Vector2Int vect = _instance._shipPlacement.IntToVector(dir);

        int i = cellPosition.x, j = cellPosition.y;
        int length = ShipDatas[CurrentShipId].length;

        if (!player.IsSpaceFree(i, j, length, vect))
            return false;

        CurrentInstanciatedChip.GetComponentInChildren<Ship>().direction = vect;
        player.AddShipToGrid(CurrentInstanciatedChip, i, j, length, vect);

        int trigDir = dir % 2 == 1 ? (dir + 2) % 4 : dir; //inverse 1 and 3 
        ClientManager.AddShip(CurrentShipId, i, j, trigDir, length);
        --NShipsToPlace;

        return true;
    }


}
