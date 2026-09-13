package com.example.jamkkaebi.friend.domain;

/**
 * 친구별 선물 버튼 상태.
 *
 */
public enum GiftState {
    AVAILABLE,     // 보낼 수 있음
    SENT,          // 이미 이 친구에게 보냄
    DONE_TODAY,    // 이미 다른 친구에게 보냄
    RECEIVER_FULL  // 상대의 선물 수신함이 가득 참
}
