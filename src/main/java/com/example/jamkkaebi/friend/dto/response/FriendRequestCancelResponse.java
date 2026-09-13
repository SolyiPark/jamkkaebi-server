package com.example.jamkkaebi.friend.dto.response;

import com.example.jamkkaebi.friend.domain.FriendRelation;

/**
 * 보낸 요청 취소의 결과.
 *
 * <p>상대 요약({@link PlayerCard})을 싣지 않는다. 취소는 없는 코드로도 성공하므로, 응답에 상대 정보가
 * 담기면 그것만으로 코드 존재 여부를 알려 주게 된다.
 *
 * @param relation  취소 후 나에게서 본 관계. 보통 {@code NONE}, 취소하는 사이 상대가 먼저 수락했다면
 *                  {@code FRIEND} (이 경우 취소된 것은 없다)
 * @param sentCount 취소 후 남은 보낸 요청 대기 수
 * @param limit     보낸 요청 대기 상한
 */
public record FriendRequestCancelResponse(FriendRelation relation, int sentCount, int limit) {
}
