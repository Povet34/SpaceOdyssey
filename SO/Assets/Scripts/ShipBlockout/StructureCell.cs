using System;
using UnityEngine;

namespace SpaceOdyssey.Blockout
{
    /// <summary>구조 칸의 역할. 뒤에 있을수록 대칭으로 겹칠 때 우선한다.</summary>
    public enum StructureRole { Fill, Armor, Tank, Engine }

    /// <summary>
    /// 구조 칸 하나(계획서 3.4절). 거주 칸과 별도 목록으로 저장되며, 걷지 못하고 벽이나 모듈을 놓을 수 없다.
    /// </summary>
    [Serializable]
    public struct StructureCell
    {
        public Vector2Int cell;
        public StructureRole role;

        [Tooltip("이 칸을 만든 규칙. Band(기본 띠), Close(움푹한 곳 메우기), 또는 형판 이름")]
        public string source;

        [Tooltip("손상 정도. 1이 가득 참")]
        [Range(0f, 1f)]
        public float durability;
    }
}
