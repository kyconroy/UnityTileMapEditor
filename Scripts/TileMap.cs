using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

public class TileMap : MonoBehaviour
{
    #region Class Variables

    public const int maxColumns = 10000;

    public float tileSize = 1;
    public Transform tilePrefab;
    public TileSet tileSet;

    public List<double> hashes = new List<double>();
    public List<Transform> prefabs = new List<Transform>();
    public List<int> directions = new List<int>();
    public List<Transform> instances = new List<Transform>();

    #endregion

    #region Helper Methods
    public double GetHash(int x, int y, int z)
    {
        double xHash = (x + maxColumns / 2);
        double yHash = (y + maxColumns / 2) * maxColumns;
        double zHash = (double)(z + maxColumns / 2) * maxColumns * maxColumns;

        return xHash + yHash + zHash;
    }

    public Vector3 GetPosition(int index)
    {
        double hash = hashes[index];
        float xValue = (float)((hash % maxColumns) - (maxColumns / 2)) * tileSize;
        float yValue = (float)((hash / maxColumns % maxColumns) - (maxColumns / 2)) * tileSize;
        float zValue = (float)((hash / maxColumns / maxColumns) - (maxColumns / 2)) * tileSize;
        Vector3 posVector = new Vector3(xValue, yValue, zValue);
        return posVector;
    }
    public void GetPosition(int index, out int x, out int y, out int z)
    {
        double hash = hashes[index];
        x = (int)((hash % maxColumns) - (maxColumns / 2));
        y = (int)((hash / maxColumns % maxColumns) - (maxColumns / 2));
        z = (int)((hash / maxColumns / maxColumns) - (maxColumns / 2));
    }

    #endregion
}
