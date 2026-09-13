package com.example.jamkkaebi.friend.domain;

/**
 * 친구 요청 상태.
 *
 * <p>수락된 요청은 행을 지우고 친구 관계로 옮기므로 상태가 없다. 만료는 {@code expiresAt} 으로 판단하므로 상태가 아니다.
 */
public enum FriendRequestStatus {
    PENDING,  // 응답 대기
    REJECTED  // 받은 쪽이 거절
}
