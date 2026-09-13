package com.example.jamkkaebi.user.domain;

import java.time.LocalDateTime;

/**
 * 사용자가 접속했다(인증된 API 를 불렀다)는 사실.
 *
 * <p>접속 시점의 상태를 남겨야 하는 기능(친구에게 보여 줄 전시관 스냅샷 등)이 구독한다. 사용자
 * 도메인이 그 기능들을 직접 부르지 않게 이벤트로 끊는다.
 *
 * @param userId     접속한 사용자
 * @param recordedAt 접속으로 기록한 시각
 */
public record UserActivityRecordedEvent(Long userId, LocalDateTime recordedAt) {
}
