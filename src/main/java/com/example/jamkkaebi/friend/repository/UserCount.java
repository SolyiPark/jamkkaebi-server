package com.example.jamkkaebi.friend.repository;

/**
 * 사용자별 집계 한 줄. 여러 사용자의 친구 수·받은 요청 수를 한 번에 셀 때 쓴다.
 *
 * @param userId 사용자
 * @param count  센 값
 */
public record UserCount(Long userId, Long count) {
}
