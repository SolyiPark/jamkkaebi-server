package com.example.jamkkaebi.artifact.repository;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.domain.UserEraCrystal;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Modifying;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.query.Param;

import java.util.List;
import java.util.Optional;

public interface UserEraCrystalRepository extends JpaRepository<UserEraCrystal, Long> {

    Optional<UserEraCrystal> findByUserIdAndEra(Long userId, Era era);

    List<UserEraCrystal> findAllByUserId(Long userId);

    /**
     * 시대의 결정을 적립한다.
     * 상자 환전과 정화 정산이 동시에 들어오면 읽어 둔 옛 잔액이 적립분을 덮어쓴다.
     */
    @Modifying(flushAutomatically = true)
    @Query("UPDATE UserEraCrystal w SET w.crystal = w.crystal + :amount WHERE w.userId = :userId AND w.era = :era")
    int addCrystal(@Param("userId") Long userId, @Param("era") Era era, @Param("amount") int amount);

    /**
     * 잔액이 충분할 때만 시대의 결정을 뺀다. 빠졌으면 1, 모자라면(또는 지갑이 없으면) 0.
     *
     * <p>잔액 검사와 차감을 한 문장에 묶어, 동시에 들어온 적립·소비가 서로의 값을 덮어쓰거나
     * 잔액이 음수로 내려가지 않게 한다.
     */
    @Modifying(flushAutomatically = true)
    @Query("""
            UPDATE UserEraCrystal w SET w.crystal = w.crystal - :amount
             WHERE w.userId = :userId AND w.era = :era AND w.crystal >= :amount
            """)
    int spendCrystal(@Param("userId") Long userId, @Param("era") Era era, @Param("amount") int amount);
}
