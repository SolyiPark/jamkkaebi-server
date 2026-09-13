package com.example.jamkkaebi.friend.dto.response;

import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * 다른 사용자 요약. 친구 목록·코드 조회·추천·요청함이 같은 모양으로 쓴다.
 *
 * @param friendCode     친구 코드 (하이픈 없음)
 * @param nickname       닉네임
 * @param spiritId       프로필 아바타 정령. 기본 아바타면 키 생략
 * @param streakDays     오늘 기준 연속 접속 일수 (끊겼으면 0)
 * @param collectionRate 수집률(%), 내림
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record PlayerCard(String friendCode, String nickname, Long spiritId, int streakDays, int collectionRate) {
}
