package com.example.jamkkaebi.friend.domain;

/** 친구 코드로 조회한 상대와 나의 관계. */
public enum FriendRelation {
    NONE,
    FRIEND,
    REQUEST_SENT,    //내가 요청을 보냄
    REQUEST_RECEIVED, //상대가 요청을 보냄
    SELF
}
