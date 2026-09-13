package com.example.jamkkaebi.friend.dto.response;

import com.fasterxml.jackson.annotation.JsonUnwrapped;

import java.time.LocalDateTime;
import java.util.List;

/**
 * 오늘의 추천.
 *
 * @param players     추천 인원 (3명)
 * @param remaining   아직 요청하지 않은 추천 수
 * @param refreshesAt 다음 추천이 채워지는 시각
 */
public record RecommendationsResponse(List<RecommendedPlayer> players, int remaining, LocalDateTime refreshesAt) {

    /**
     * @param player        추천 사용자 요약
     * @param restoredByEra 시대별 완료 유물 수
     * @param requested     오늘 이 사람에게 요청을 보냈는지
     */
    public record RecommendedPlayer(
            @JsonUnwrapped PlayerCard player,
            List<EraCount> restoredByEra,
            boolean requested
    ) {
    }
}
