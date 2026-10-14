package com.example.jamkkaebi.relic.repository;

import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.domain.UserRelic;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.Collection;
import java.util.List;
import java.util.Optional;

public interface UserRelicRepository extends JpaRepository<UserRelic, Long> {

    Optional<UserRelic> findByUserIdAndRelicId(Long userId, Integer relicId);

    List<UserRelic> findAllByUserId(Long userId);

    List<UserRelic> findAllByUserIdAndStatus(Long userId, RelicStatus status);

    long countByUserIdAndStatus(Long userId, RelicStatus status);

    // 이미 보유한 유물 번호. 상자가 신규/중복 풀을 결정할 때 쓴다.
    @Query("SELECT ur.relicId FROM UserRelic ur WHERE ur.userId = :userId")
    List<Integer> findRelicIdsByUserId(@Param("userId") Long userId);

    /**
     * 시대별 완료 유물 수
     *
     * <p>친구 목록의 수집률과 도감 진행률이 이 집계를 공유한다.
     */
    @Query("""
            SELECT new com.example.jamkkaebi.relic.repository.EraCompletion(ur.userId, r.era, COUNT(ur))
              FROM UserRelic ur
              JOIN RelicMaster r ON r.id = ur.relicId
             WHERE ur.userId IN :userIds AND ur.status = com.example.jamkkaebi.relic.domain.RelicStatus.COMPLETED
             GROUP BY ur.userId, r.era
            """)
    List<EraCompletion> countCompletedByEra(@Param("userIds") Collection<Long> userIds);
}
